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
        const string sql = """
                           SELECT
                               id,
                               user_id,
                               token_hash,
                               created_at,
                               expires_at,
                               revoked_at
                           FROM refresh_tokens
                           WHERE user_id = @UserId
                             AND revoked_at IS NULL
                             AND expires_at > UTC_TIMESTAMP()
                           ORDER BY created_at ASC;
                           """;

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
        return affectedRows == 1;
    }

    public async Task<RefreshToken?> FindByHashAsync(string tokenHash)
    {
        const string sql = """
            SELECT
                id,
                user_id,
                token_hash,
                expires_at,
                created_at,
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
        const string sql = """
           UPDATE refresh_tokens
           SET revoked_at = @RevokedAt
           WHERE id = @Id
             AND revoked_at IS NULL;
           """;

        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync();

        await using var command = new MySqlCommand(sql, connection);
        command.AddParam("@Id", refreshTokenId);
        command.AddParam("@RevokedAt", revokedAt);

        var affectedRows = await command.ExecuteNonQueryAsync();

        return affectedRows > 0;
    }

    public async Task<bool> TryIssueAsync(User expectedUser, RefreshToken replacement, string? predecessorHash,
        int maxSessions, CancellationToken cancellationToken = default)
    {
        await using var c = connectionFactory.CreateConnection(); await c.OpenAsync(cancellationToken);
        await using var tx = await c.BeginTransactionAsync(cancellationToken);
        User? current;
        await using (var read = new MySqlCommand("SELECT " + UserRepository.Columns + " FROM users WHERE user_id=@id FOR UPDATE", c, tx))
        {
            read.Parameters.AddWithValue("id", expectedUser.Id);
            await using var r = await read.ExecuteReaderAsync(cancellationToken);
            current = await r.ReadAsync(cancellationToken) ? UserRepository.ReadUser(r) : null;
        }
        if (current is null || current.DeletedAt.HasValue || current.IsLockedOut ||
            current.PasswordHash != expectedUser.PasswordHash || current.Email != expectedUser.Email ||
            current.UserRole != expectedUser.UserRole || current.PasswordChanged != expectedUser.PasswordChanged ||
            current.TwoFactorEnabled != expectedUser.TwoFactorEnabled) return false;
        if (predecessorHash is not null && await c.ExecuteAsync(new CommandDefinition("""
            UPDATE refresh_tokens SET revoked_at=UTC_TIMESTAMP(6) WHERE user_id=@id AND token_hash=@hash
                AND revoked_at IS NULL AND expires_at>UTC_TIMESTAMP(6)
            """, new { id = current.Id, hash = predecessorHash }, tx, cancellationToken: cancellationToken)) != 1) return false;
        var ids = (await c.QueryAsync<Guid>(new CommandDefinition("""
            SELECT id FROM refresh_tokens WHERE user_id=@id AND revoked_at IS NULL AND expires_at>UTC_TIMESTAMP(6) ORDER BY created_at,id
            """, new { id = current.Id }, tx, cancellationToken: cancellationToken))).ToList();
        foreach (var id in ids.Take(Math.Max(0, ids.Count - maxSessions + 1)))
            await c.ExecuteAsync(new CommandDefinition("UPDATE refresh_tokens SET revoked_at=UTC_TIMESTAMP(6) WHERE id=@id", new { id }, tx, cancellationToken: cancellationToken));
        await c.ExecuteAsync(new CommandDefinition("""
            INSERT INTO refresh_tokens(id,user_id,token_hash,created_at,expires_at) VALUES(@Id,@UserId,@TokenHash,@CreatedAt,@ExpiresAt)
            """, replacement, tx, cancellationToken: cancellationToken));
        await tx.CommitAsync(cancellationToken);
        return true;
    }
}

