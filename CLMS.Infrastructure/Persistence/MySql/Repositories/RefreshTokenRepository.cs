using CLMS.Infrastructure.Persistence.MySql.Utils;
using CLMS.Application.Persistence.Interfaces;
using CLMS.Domain.Entities.Identity;
using MySqlConnector;
using Dapper;

namespace CLMS.Infrastructure.Persistence.MySql.Repositories;

public class RefreshTokenRepository(MySqlConnectionFactory connectionFactory) : IRefreshTokenRepository
{
    public async Task<IReadOnlyList<RefreshToken>> GetActiveByUserIdAsync(Guid userId)
    {
        const string sql = "CALL sp_refresh_token_get_active_by_user_id(@UserId);";

        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync();

        await using var command = new MySqlCommand(sql, connection);
        command.AddParam("@UserId", userId);

        await using var reader = await command.ExecuteReaderAsync();

        var refreshTokens = new List<RefreshToken>();

        while (await reader.ReadAsync())
        {
            DateTime? revokedAT = reader.IsDBNull(reader.GetOrdinal("revoked_at")) 
                ? null
                : reader.GetDateTime("revoked_at");
            
            refreshTokens.Add(
                new RefreshToken(
                    reader.GetGuid("id"),
                    reader.GetGuid("user_id"),
                    reader.GetString("token_hash"),
                    reader.GetDateTime("created_at"),
                    reader.GetDateTime("expires_at"),
                    revokedAT
                )
            );
        }

        return refreshTokens;
    }

    public async Task<bool> AddAsync(RefreshToken refreshToken)
    {
        // define SQL
        const string sql = "CALL sp_refresh_token_add(@Id, @UserId, @TokenHash, @ExpiresAt, @RevokedAt);";

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
        var affectedRows = await command.ExecuteAffectedRowsAsync();
        return affectedRows == 1;
    }

    public async Task<RefreshToken?> FindByHashAsync(string tokenHash)
    {
        const string sql = "CALL sp_refresh_token_find_by_hash(@TokenHash);";
        
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
                reader.GetDateTime("created_at"),
                reader.GetDateTime("expires_at"),
                reader.IsDBNull(reader.GetOrdinal("revoked_at")) ? null : reader.GetDateTime("revoked_at")
            );
        }

        return null;
    }

    public async Task<bool> RevokeAsync(
        Guid refreshTokenId,
        DateTime revokedAt)
    {
        const string sql = "CALL sp_refresh_token_revoke(@RevokedAt, @Id);";

        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync();

        await using var command = new MySqlCommand(sql, connection);
        command.AddParam("@Id", refreshTokenId);
        command.AddParam("@RevokedAt", revokedAt);

        var affectedRows = await command.ExecuteAffectedRowsAsync();

        return affectedRows > 0;
    }

    public async Task<bool> TryIssueAsync(User expectedUser, RefreshToken replacement, string? predecessorHash,
        int maxSessions, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new MySqlCommand("CALL sp_refresh_token_try_issue(@p0, @p1, @p2, @p3, @p4, @p5, @p6, @p7, @p8, @p9, @p10, @p11, @p12);", connection);
        command.Parameters.AddWithValue("p0", expectedUser.Id);
        command.Parameters.AddWithValue("p1", expectedUser.PasswordHash);
        command.Parameters.AddWithValue("p2", expectedUser.Email);
        command.Parameters.AddWithValue("p3", expectedUser.UserRole.ToString());
        command.Parameters.AddWithValue("p4", expectedUser.PasswordChanged);
        command.Parameters.AddWithValue("p5", expectedUser.TwoFactorEnabled);
        command.Parameters.AddWithValue("p6", replacement.Id);
        command.Parameters.AddWithValue("p7", replacement.UserId);
        command.Parameters.AddWithValue("p8", replacement.TokenHash);
        command.Parameters.AddWithValue("p9", replacement.CreatedAt);
        command.Parameters.AddWithValue("p10", replacement.ExpiresAt);
        command.Parameters.AddWithValue("p11", (object?)predecessorHash ?? DBNull.Value);
        command.Parameters.AddWithValue("p12", maxSessions);
        return await command.ExecuteAffectedRowsAsync(cancellationToken) == 1;
    }
}

