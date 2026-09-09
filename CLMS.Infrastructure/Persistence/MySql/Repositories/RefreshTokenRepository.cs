using CLMS.Infrastructure.Persistence.MySql.Utils;
using CLMS.Application.Persistence.Interfaces;
using CLMS.Domain.Entities.Authentication;
using MySqlConnector;

namespace CLMS.Infrastructure.Persistence.MySql.Repositories;

public class RefreshTokenRepository(MySqlConnectionFactory connectionFactory) : IRefreshTokenRepository
{
    public async Task<bool> AddAsync(RefreshToken refreshToken)
    {
        // define SQL
        const string sql = """
            INSERT INTO refresh_tokens
                (id, user_id, token_hash, expires_at, revoked_at)
            VALUES
                (@Id, @UserId, @TokenHash, @ExpiresAt, @RevokedAt);
            """;

        // open connection
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync();
        
        // prepare command
        await using var command = new MySqlCommand(sql, connection);
        command.AddParam("@Id", refreshToken.Id);
        command.AddParam("@UserId", refreshToken.UserId);
        command.AddParam("@TokenHash", refreshToken.TokenHash);
        command.AddParam("@ExpiresAt", refreshToken.ExpiresAt);
        command.AddParam("@RevokedAt", refreshToken.RevokedAt);
        
        // execute sql
        var affectedRows = await command.ExecuteNonQueryAsync();
        return affectedRows == 0;
    }

    public async Task<RefreshToken?> FindByHashAsync(string tokenHash)
    {
        const string sql = """
            SELECT
                id,
                user_id,
                token_hash,
                expires_at,
                revoked_at
            FROM refresh_tokens
            WHERE token_hash = @TokenHash
            ORDER BY created_at DESC 
            LIMIT 1;
            """;
        
        // connection
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync();
        
        // command
        await using var command = new MySqlCommand(sql, connection);
        command.AddParam("@TokenHash", tokenHash);
        
        // reader
        await using var reader = await command.ExecuteReaderAsync();

        if (await reader.ReadAsync())
        {
            return new RefreshToken(
                reader.GetGuid("id"),
                reader.GetGuid("user_id"),
                reader.GetString("token_hash"),
                reader.GetDateTime("expires_at"),
                reader.GetDateTime("revoked_at")
            );
        }

        return null;
    }

    public async Task<bool> UpdateAsync(RefreshToken refreshToken)
    {
        const string sql = """
            UPDATE refresh_tokens
            SET
                revoked_at = @RevokedAt
            WHERE id = @Id;
            """;

        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync();
        
        await using var command = new MySqlCommand(sql, connection);
        command.AddParam("@Id", refreshToken.Id);
        command.AddParam("@RevokedAt", refreshToken.RevokedAt);
        
        var affectedRows = await command.ExecuteNonQueryAsync();
        return affectedRows > 0;
    }
}

