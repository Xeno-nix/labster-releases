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
        await using var cmd = new MySqlCommand("CALL sp_user_add(@id,@first,@last,@middle,@suffix,@email,@hash,@role,@changed,@twoFactor);", c);
        AddDetails(cmd, user);
        cmd.Parameters.AddWithValue("hash", user.PasswordHash);
        cmd.Parameters.AddWithValue("changed", user.PasswordChanged);
        cmd.Parameters.AddWithValue("twoFactor", user.TwoFactorEnabled);
        try { return await cmd.ExecuteAffectedRowsAsync() == 1; }
        catch (MySqlException ex) when (ex.Number == 1062)
        { throw new InvalidOperationException("A user with this email already exists."); }
    }

    public async Task<bool> DeleteAsync(User user)
    {
        await using var c = connectionFactory.CreateConnection(); await c.OpenAsync();
        await using var cmd = new MySqlCommand("CALL sp_user_delete(@id);", c);
        cmd.Parameters.AddWithValue("id", user.Id);
        return await cmd.ExecuteAffectedRowsAsync() == 1;
    }

    public async Task<IReadOnlyList<User>> FindAllAsync()
    {
        await using var c = connectionFactory.CreateConnection(); await c.OpenAsync();
        await using var cmd = new MySqlCommand("CALL sp_user_find_all();", c);
        await using var r = await cmd.ExecuteReaderAsync();
        var result = new List<User>();
        while (await r.ReadAsync()) result.Add(ReadUser(r));
        return result;
    }

    public async Task<IReadOnlyList<User>> FindAllByRoleAsync(Role role)
    {
        await using var c = connectionFactory.CreateConnection(); await c.OpenAsync();
        await using var cmd = new MySqlCommand("CALL sp_user_find_all_by_role(@role);", c);
        cmd.Parameters.AddWithValue("role", role.ToString());
        await using var r = await cmd.ExecuteReaderAsync();
        var result = new List<User>();
        while (await r.ReadAsync()) result.Add(ReadUser(r));
        return result;
    }

    public async Task<User?> FindByEmailAsync(string email)
    {
        await using var c = connectionFactory.CreateConnection(); await c.OpenAsync();
        await using var cmd = new MySqlCommand("CALL sp_user_find_by_email(@email);", c);
        cmd.Parameters.AddWithValue("email", email.Trim().ToLowerInvariant());
        await using var r = await cmd.ExecuteReaderAsync();
        return await r.ReadAsync() ? ReadUser(r) : null;
    }

    public async Task<User?> FindByIdAsync(Guid id)
    {
        await using var c = connectionFactory.CreateConnection(); await c.OpenAsync();
        await using var cmd = new MySqlCommand("CALL sp_user_find_by_id(@id);", c);
        cmd.Parameters.AddWithValue("id", id);
        await using var r = await cmd.ExecuteReaderAsync();
        return await r.ReadAsync() ? ReadUser(r) : null;
    }

    public async Task<bool> UpdateAsync(User user)
    {
        await using var c = connectionFactory.CreateConnection(); await c.OpenAsync();
        // Profile edits must not overwrite a newer password or change its first-login flag.
        await using var cmd = new MySqlCommand("CALL sp_user_update(@id,@first,@last,@middle,@suffix,@email,@role);", c);
        AddDetails(cmd, user);
        return await cmd.ExecuteAffectedRowsAsync() == 1;
    }

    public async Task<bool> ChangePasswordAsync(Guid userId, string expectedHash, string newHash)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new MySqlCommand("CALL sp_user_change_password(@p0, @p1, @p2);", connection);
        command.Parameters.AddWithValue("p0", userId);
        command.Parameters.AddWithValue("p1", expectedHash);
        command.Parameters.AddWithValue("p2", newHash);
        return await command.ExecuteAffectedRowsAsync() == 1;
    }
    
    public async Task<bool> ChangeTwoFactorAsync(User expectedUser, bool enabled, string replacementPasswordHash, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new MySqlCommand("CALL sp_user_change_two_factor(@p0, @p1, @p2, @p3, @p4, @p5, @p6);", connection);
        command.Parameters.AddWithValue("p0", expectedUser.Id);
        command.Parameters.AddWithValue("p1", enabled);
        command.Parameters.AddWithValue("p2", replacementPasswordHash);
        command.Parameters.AddWithValue("p3", expectedUser.PasswordHash);
        command.Parameters.AddWithValue("p4", expectedUser.Email);
        command.Parameters.AddWithValue("p5", expectedUser.UserRole.ToString());
        command.Parameters.AddWithValue("p6", expectedUser.TwoFactorEnabled);
        return await command.ExecuteAffectedRowsAsync(cancellationToken) == 1;
    }

    public async Task<int> IncrementFailedLoginAttemptsAsync(Guid userId)
    {
        // the increment happens in SQL so concurrent failed logins can't overwrite each other's count
        const string sql = "CALL sp_user_increment_failed_login_attempts(@UserId);";

        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync();

        await using var command = new MySqlCommand(sql, connection);
        command.AddParam("@UserId", userId);

        var result = await command.ExecuteScalarAsync();
        return Convert.ToInt32(result);
    }

    public async Task<bool> LockOutAsync(Guid userId, DateTime lockoutEnd)
    {
        const string sql = "CALL sp_user_lock_out(@LockoutEnd, @UserId);";

        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync();

        await using var command = new MySqlCommand(sql, connection);
        command.AddParam("@UserId", userId);
        command.AddParam("@LockoutEnd", lockoutEnd);

        var rowsAffected = await command.ExecuteAffectedRowsAsync();
        return rowsAffected == 1;
    }

    public async Task<bool> ResetFailedLoginAttemptsAsync(Guid userId)
    {
        const string sql = "CALL sp_user_reset_failed_login_attempts(@UserId);";

        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync();

        await using var command = new MySqlCommand(sql, connection);
        command.AddParam("@UserId", userId);

        var rowsAffected = await command.ExecuteAffectedRowsAsync();
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
        new Name(r.IsDBNull(r.GetOrdinal("first_name")) ? "Unknown" : r.GetString("first_name")),
        new Name(r.IsDBNull(r.GetOrdinal("last_name")) ? "Unknown" : r.GetString("last_name")),
        r.IsDBNull(r.GetOrdinal("middle_name")) ? null : new Name(r.GetString("middle_name")),
        r.IsDBNull(r.GetOrdinal("suffix")) ? null : new Name(r.GetString("suffix")),
        r.IsDBNull(r.GetOrdinal("email")) ? "" : r.GetString("email"),
        !r.IsDBNull(r.GetOrdinal("role_type")) && Enum.TryParse<Role>(r.GetString("role_type"), true, out var role) && Enum.IsDefined(role) ? role : Role.Unassigned,
        r.IsDBNull(r.GetOrdinal("password_hash")) ? "" : r.GetString("password_hash"),
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
