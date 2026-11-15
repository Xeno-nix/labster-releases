using CLMS.Application.Persistence.Interfaces;
using CLMS.Domain.Entities.Identity;
using CLMS.Domain.Enums;
using CLMS.Domain.ValueObjects;
using CLMS.Infrastructure.Persistence.MySql.Utils;
using MySqlConnector;

namespace CLMS.Infrastructure.Persistence.MySql.Repositories;

public class UserRepository(MySqlConnectionFactory connectionFactory) : IUserRepository
{
    internal const string Columns = "user_id,first_name,last_name,middle_name,suffix,email,role_type,password_hash,password_changed,created_at,last_modified_at,deleted_at,failed_login_attempts,lockout_end,two_factor_enabled";

    public async Task<bool> AddAsync(User user)
    {
        await using var c = connectionFactory.CreateConnection(); await c.OpenAsync();
        await using var cmd = new MySqlCommand("""
            INSERT INTO users(user_id,first_name,last_name,middle_name,suffix,email,password_hash,role_type,password_changed,two_factor_enabled)
            VALUES(@id,@first,@last,@middle,@suffix,@email,@hash,@role,@changed,@twoFactor)
            """, c);
        AddDetails(cmd, user);
        cmd.Parameters.AddWithValue("hash", user.PasswordHash);
        cmd.Parameters.AddWithValue("changed", user.PasswordChanged);
        cmd.Parameters.AddWithValue("twoFactor", user.TwoFactorEnabled);
        try { return await cmd.ExecuteNonQueryAsync() == 1; }
        catch (MySqlException ex) when (ex.Number == 1062)
        { throw new InvalidOperationException("A user with this email already exists."); }
    }

    public async Task<bool> DeleteAsync(User user)
    {
        await using var c = connectionFactory.CreateConnection(); await c.OpenAsync();
        await using var cmd = new MySqlCommand("UPDATE users SET deleted_at=UTC_TIMESTAMP() WHERE user_id=@id", c);
        cmd.Parameters.AddWithValue("id", user.Id);
        return await cmd.ExecuteNonQueryAsync() == 1;
    }

    public async Task<IReadOnlyList<User>> FindAllAsync()
    {
        await using var c = connectionFactory.CreateConnection(); await c.OpenAsync();
        await using var cmd = new MySqlCommand("SELECT " + Columns + " FROM users", c);
        await using var r = await cmd.ExecuteReaderAsync();
        var result = new List<User>();
        while (await r.ReadAsync()) result.Add(ReadUser(r));
        return result;
    }

    public async Task<IReadOnlyList<User>> FindAllByRoleAsync(Role role)
    {
        await using var c = connectionFactory.CreateConnection(); await c.OpenAsync();
        await using var cmd = new MySqlCommand("SELECT " + Columns + " FROM users WHERE role_type=@role", c);
        cmd.Parameters.AddWithValue("role", role.ToString());
        await using var r = await cmd.ExecuteReaderAsync();
        var result = new List<User>();
        while (await r.ReadAsync()) result.Add(ReadUser(r));
        return result;
    }

    public async Task<User?> FindByEmailAsync(string email)
    {
        await using var c = connectionFactory.CreateConnection(); await c.OpenAsync();
        await using var cmd = new MySqlCommand("SELECT " + Columns + " FROM users WHERE LOWER(TRIM(email))=@email LIMIT 1", c);
        cmd.Parameters.AddWithValue("email", email.Trim().ToLowerInvariant());
        await using var r = await cmd.ExecuteReaderAsync();
        return await r.ReadAsync() ? ReadUser(r) : null;
    }

    public async Task<User?> FindByIdAsync(Guid id)
    {
        await using var c = connectionFactory.CreateConnection(); await c.OpenAsync();
        await using var cmd = new MySqlCommand("SELECT " + Columns + " FROM users WHERE user_id=@id LIMIT 1", c);
        cmd.Parameters.AddWithValue("id", id);
        await using var r = await cmd.ExecuteReaderAsync();
        return await r.ReadAsync() ? ReadUser(r) : null;
    }

    public async Task<bool> UpdateAsync(User user)
    {
        await using var c = connectionFactory.CreateConnection(); await c.OpenAsync();
        // Profile edits must not overwrite a newer password or change its first-login flag.
        await using var cmd = new MySqlCommand("""
            UPDATE users SET first_name=@first,last_name=@last,middle_name=@middle,suffix=@suffix,
            reset_token=IF(BINARY email=BINARY @email,reset_token,NULL),
            reset_token_expiration=IF(BINARY email=BINARY @email,reset_token_expiration,NULL),email=@email,
            role_type=@role,last_modified_at=UTC_TIMESTAMP() WHERE user_id=@id AND deleted_at IS NULL
            """, c);
        AddDetails(cmd, user);
        return await cmd.ExecuteNonQueryAsync() == 1;
    }

    public async Task<bool> ChangePasswordAsync(Guid userId, string expectedHash, string newHash)
    {
        await using var c = connectionFactory.CreateConnection(); await c.OpenAsync();
        await using var tx = await c.BeginTransactionAsync();
        // Compare the previous hash exactly: only one concurrent password change may succeed.
        await using var cmd = new MySqlCommand("""
            UPDATE users SET password_hash=@newHash,password_changed=1,last_modified_at=UTC_TIMESTAMP(),reset_token=NULL,reset_token_expiration=NULL
            WHERE user_id=@id AND deleted_at IS NULL AND BINARY password_hash=BINARY @expectedHash
            """, c, tx);
        cmd.Parameters.AddWithValue("id", userId);
        cmd.Parameters.AddWithValue("expectedHash", expectedHash);
        cmd.Parameters.AddWithValue("newHash", newHash);
        if (await cmd.ExecuteNonQueryAsync() != 1) return false;
        await using var revoke = new MySqlCommand("UPDATE refresh_tokens SET revoked_at=UTC_TIMESTAMP(6) WHERE user_id=@id AND revoked_at IS NULL", c, tx);
        revoke.Parameters.AddWithValue("id", userId); await revoke.ExecuteNonQueryAsync();
        await tx.CommitAsync();
        return true;
    }
    
    public async Task<bool> ChangeTwoFactorAsync(User expectedUser, bool enabled, string replacementPasswordHash, CancellationToken cancellationToken = default)
    {
        await using var c = connectionFactory.CreateConnection(); await c.OpenAsync(cancellationToken);
        await using var tx = await c.BeginTransactionAsync(cancellationToken);
        // UPDATE locks the same user row as reset and refresh, then compares the entire security snapshot.
        await using var change = new MySqlCommand("""
            UPDATE users SET two_factor_enabled=@enabled,password_hash=@replacement,
                reset_token=NULL,reset_token_expiration=NULL,last_modified_at=UTC_TIMESTAMP()
            WHERE user_id=@id AND deleted_at IS NULL AND (lockout_end IS NULL OR lockout_end<=UTC_TIMESTAMP())
                AND BINARY password_hash=BINARY @expectedHash AND BINARY email=BINARY @expectedEmail
                AND role_type=@expectedRole AND password_changed=1 AND two_factor_enabled=@expectedSetting
            """, c, tx);
        change.Parameters.AddWithValue("id", expectedUser.Id); change.Parameters.AddWithValue("enabled", enabled);
        change.Parameters.AddWithValue("replacement", replacementPasswordHash); change.Parameters.AddWithValue("expectedHash", expectedUser.PasswordHash);
        change.Parameters.AddWithValue("expectedEmail", expectedUser.Email); change.Parameters.AddWithValue("expectedRole", expectedUser.UserRole.ToString());
        change.Parameters.AddWithValue("expectedSetting", expectedUser.TwoFactorEnabled);
        if (await change.ExecuteNonQueryAsync(cancellationToken) != 1) return false;
        await using var revoke = new MySqlCommand("UPDATE refresh_tokens SET revoked_at=UTC_TIMESTAMP(6) WHERE user_id=@id AND revoked_at IS NULL", c, tx);
        revoke.Parameters.AddWithValue("id", expectedUser.Id); await revoke.ExecuteNonQueryAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return true;
    }

    public async Task<int> IncrementFailedLoginAttemptsAsync(Guid userId)
    {
        // the increment happens in SQL so concurrent failed logins can't overwrite each other's count
        const string sql = """
            UPDATE users
            SET failed_login_attempts = failed_login_attempts + 1
            WHERE user_id = @UserId;

            SELECT failed_login_attempts
            FROM users
            WHERE user_id = @UserId;
            """;

        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync();

        await using var command = new MySqlCommand(sql, connection);
        command.AddParam("@UserId", userId);

        var result = await command.ExecuteScalarAsync();
        return Convert.ToInt32(result);
    }

    public async Task<bool> LockOutAsync(Guid userId, DateTime lockoutEnd)
    {
        const string sql = """
            UPDATE users
            SET
                failed_login_attempts = 0,
                lockout_end = @LockoutEnd
            WHERE user_id = @UserId;
            """;

        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync();

        await using var command = new MySqlCommand(sql, connection);
        command.AddParam("@UserId", userId);
        command.AddParam("@LockoutEnd", lockoutEnd);

        var rowsAffected = await command.ExecuteNonQueryAsync();
        return rowsAffected == 1;
    }

    public async Task<bool> ResetFailedLoginAttemptsAsync(Guid userId)
    {
        const string sql = """
            UPDATE users
            SET
                failed_login_attempts = 0,
                lockout_end = NULL
            WHERE user_id = @UserId;
            """;

        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync();

        await using var command = new MySqlCommand(sql, connection);
        command.AddParam("@UserId", userId);

        var rowsAffected = await command.ExecuteNonQueryAsync();
        return rowsAffected == 1;
    }

    private static void AddDetails(MySqlCommand cmd, User user)
    {
        cmd.Parameters.AddWithValue("id", user.Id);
        cmd.Parameters.AddWithValue("first", user.FirstName.Value);
        cmd.Parameters.AddWithValue("last", user.LastName.Value);
        cmd.Parameters.AddWithValue("middle", (object?)user.MiddleName?.Value ?? DBNull.Value);
        cmd.Parameters.AddWithValue("suffix", (object?)user.Suffix?.Value ?? DBNull.Value);
        cmd.Parameters.AddWithValue("email", user.Email);
        cmd.Parameters.AddWithValue("role", user.UserRole.ToString());
    }

    internal static User ReadUser(MySqlDataReader r) =>
        new(r.GetGuid("user_id"), 
        new Name(r.GetString("first_name")),
        new Name(r.GetString("last_name")), 
        r.IsDBNull(r.GetOrdinal("middle_name")) ? null : new Name(r.GetString("middle_name")),
        r.IsDBNull(r.GetOrdinal("suffix")) ? null : new Name(r.GetString("suffix")),
        r.GetString("email"),
        Enum.Parse<Role>(r.GetString("role_type"), true),
        r.GetString("password_hash"), 
        r.GetDateTime("created_at"),
        r.GetDateTime("last_modified_at"), 
        r.IsDBNull(r.GetOrdinal("deleted_at")) ? null : r.GetDateTime("deleted_at"))
        {
            PasswordChanged = r.GetBoolean("password_changed"),
            TwoFactorEnabled = r.GetBoolean("two_factor_enabled"),
            FailedLoginAttempts = r.GetInt32("failed_login_attempts"),
            LockoutEnd = r.IsDBNull(r.GetOrdinal("lockout_end"))
                ? null
                : r.GetDateTime("lockout_end")
        };
}
