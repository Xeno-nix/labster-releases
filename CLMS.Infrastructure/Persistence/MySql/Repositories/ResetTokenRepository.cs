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

        if (await reader.ReadAsync())
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
        // TODO : Set expiry date in config?
        
        // define SQL
        const string sql = """
           UPDATE users
           SET
               reset_token = @ResetToken,
               reset_token_expiration = @ResetToken_ExpiresAt
           WHERE 
               user_id = @UserId;
           """;

        // open connection
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync();
        
        // prepare command
        await using var command = new MySqlCommand(sql, connection);
        command.AddParam("@UserId", resetToken.UserId);
        command.AddParam("@TokenHash", resetToken.TokenHash);
        command.AddParam("@ExpiresAt", DateTime.UtcNow.AddMinutes(10));
        
        // execute sql
        var affectedRows = await command.ExecuteNonQueryAsync();
        return affectedRows == 0;
    }
}