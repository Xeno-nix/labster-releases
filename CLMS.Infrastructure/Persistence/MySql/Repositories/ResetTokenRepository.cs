using CLMS.Infrastructure.Persistence.MySql.Utils;
using CLMS.Application.Persistence.Interfaces;
using CLMS.Domain.Entities.Identity;
using MySqlConnector;

namespace CLMS.Infrastructure.Persistence.MySql.Repositories;

public class ResetTokenRepository(MySqlConnectionFactory connectionFactory) : IResetTokenRepository
{
    public async Task<ResetToken?> FindByUserIdAsync(string userId)
    {
        const string sql = "CALL sp_reset_token_find_by_user_id(@UserId);";
        
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
        const string sql = "CALL sp_reset_token_create(@ResetToken, @ResetToken_ExpiresAt, @UserId, @Email);";

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
        var affectedRows = await command.ExecuteAffectedRowsAsync();
        return affectedRows == 1;
    }

    public async Task<bool> ConsumeAsync(Guid userId, string expectedTokenHash, string expectedPasswordHash,
        string newPasswordHash, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new MySqlCommand("CALL sp_reset_token_consume(@p0, @p1, @p2, @p3);", connection);
        command.Parameters.AddWithValue("p0", userId);
        command.Parameters.AddWithValue("p1", expectedTokenHash);
        command.Parameters.AddWithValue("p2", expectedPasswordHash);
        command.Parameters.AddWithValue("p3", newPasswordHash);
        return await command.ExecuteAffectedRowsAsync(cancellationToken) == 1;
    }
}
