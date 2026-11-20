using CLMS.Infrastructure.Persistence.MySql.Utils;
using CLMS.Application.Persistence.Interfaces;
using CLMS.Domain.Entities.Identity;
using MySqlConnector;

namespace CLMS.Infrastructure.Persistence.MySql.Repositories;

public class ResetTokenRepository(MySqlConnectionFactory connectionFactory) : IResetTokenRepository
{
    public async Task<ResetToken?> FindByUserIdAsync(string userId)
    {
        const string sql = """
           SELECT
               user_id,
               reset_token,
               reset_token_expiration
           FROM users
           WHERE user_id = @UserId
           LIMIT 1;
           """;
        
        // connection
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync();
        
        // command
        await using var command = new MySqlCommand(sql, connection);
        command.AddParam("@UserId", userId);
        
        // reader
        await using var reader = await command.ExecuteReaderAsync();

        if (await reader.ReadAsync() && !reader.IsDBNull(reader.GetOrdinal("reset_token")) && !reader.IsDBNull(reader.GetOrdinal("reset_token_expiration")))
        {
            return new ResetToken(
                reader.GetGuid("user_id"),
                reader.GetString("reset_token"),
                reader.GetDateTime("reset_token_expiration")
            );
        }

        return null;
    }

    public async Task<bool> CreateAsync(ResetToken resetToken)
    {
        if (resetToken.TokenHash.Length != 64 || !resetToken.TokenHash.All(char.IsAsciiHexDigit))
            throw new ArgumentException("A SHA-256 reset-token hash is required.", nameof(resetToken));
        
        // define SQL
        const string sql = """
           UPDATE users
           SET
               reset_token = @ResetToken,
               reset_token_expiration = @ResetToken_ExpiresAt
           WHERE 
               user_id = @UserId AND deleted_at IS NULL AND BINARY email=BINARY @Email;
           """;

        // open connection
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync();
        
        // prepare command
        await using var command = new MySqlCommand(sql, connection);
        command.AddParam("@UserId", resetToken.UserId);
        command.AddParam("@ResetToken", resetToken.TokenHash);
        command.AddParam("@ResetToken_ExpiresAt", resetToken.ExpiresAt);
        command.AddParam("@Email", resetToken.Email);
        
        // execute sql
        var affectedRows = await command.ExecuteNonQueryAsync();
        return affectedRows == 1;
    }

    public async Task<bool> ConsumeAsync(Guid userId, string expectedTokenHash, string expectedPasswordHash,
        string newPasswordHash, CancellationToken cancellationToken = default)
    {
        await using var c = connectionFactory.CreateConnection(); await c.OpenAsync(cancellationToken);
        await using var tx = await c.BeginTransactionAsync(cancellationToken);
        await using var change = new MySqlCommand("""
            UPDATE users SET password_hash=@newPasswordHash,password_changed=1,reset_token=NULL,reset_token_expiration=NULL,
                failed_login_attempts=0,lockout_end=NULL,last_modified_at=UTC_TIMESTAMP()
            WHERE user_id=@userId AND deleted_at IS NULL AND BINARY reset_token=BINARY @expectedTokenHash
                AND BINARY password_hash=BINARY @expectedPasswordHash
                AND reset_token_expiration>UTC_TIMESTAMP()
            """, c, tx);
        change.Parameters.AddWithValue("userId", userId); change.Parameters.AddWithValue("newPasswordHash", newPasswordHash);
        change.Parameters.AddWithValue("expectedTokenHash", expectedTokenHash);
        change.Parameters.AddWithValue("expectedPasswordHash", expectedPasswordHash);
        if (await change.ExecuteNonQueryAsync(cancellationToken) != 1) return false;
        await using var revoke = new MySqlCommand("UPDATE refresh_tokens SET revoked_at=UTC_TIMESTAMP(6) WHERE user_id=@id AND revoked_at IS NULL", c, tx);
        revoke.Parameters.AddWithValue("id", userId); await revoke.ExecuteNonQueryAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return true;
    }
}
