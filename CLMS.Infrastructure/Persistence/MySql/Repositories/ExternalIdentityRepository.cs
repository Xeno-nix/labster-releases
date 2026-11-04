using CLMS.Application.Persistence.Interfaces;
using CLMS.Domain.Entities.Identity;
using CLMS.Infrastructure.Persistence.MySql.Utils;

using MySqlConnector;

namespace CLMS.Infrastructure.Persistence.MySql.Repositories;

public class ExternalIdentityRepository(MySqlConnectionFactory connectionFactory) : IExternalIdentityRepository
{
    /// <summary>Serializes linking for an account and relies on unique indexes for cross-account subject races.</summary>
    public async Task LinkGoogleAsync(Guid userId, string email, string subject, string expectedPasswordHash,
        CancellationToken cancellationToken = default)
    {
        await using var c = connectionFactory.CreateConnection();
        await c.OpenAsync(cancellationToken);
        await using var t = await c.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted, cancellationToken);
        await using var user = new MySqlCommand("""
            SELECT user_id FROM users WHERE user_id=@user AND deleted_at IS NULL
            AND LOWER(TRIM(email))=@email AND BINARY password_hash=BINARY @hash FOR UPDATE
            """, c, t);
        user.Parameters.AddWithValue("user", userId);
        user.Parameters.AddWithValue("email", email.Trim().ToLowerInvariant());
        user.Parameters.AddWithValue("hash", expectedPasswordHash);
        if (await user.ExecuteScalarAsync(cancellationToken) is null)
            throw new InvalidOperationException("The account changed. Sign in again and retry.");

        await using var existing = new MySqlCommand("""
            SELECT user_id,provider_sub FROM external_identities
            WHERE provider='google' AND (user_id=@user OR provider_sub=@subject) FOR UPDATE
            """, c, t);
        existing.Parameters.AddWithValue("user", userId);
        existing.Parameters.AddWithValue("subject", subject);
        var alreadyLinked = false;
        await using (var r = await existing.ExecuteReaderAsync(cancellationToken))
        {
            while (await r.ReadAsync(cancellationToken))
            {
                if (r.GetGuid(0) != userId || r.GetString(1) != subject)
                    throw new InvalidOperationException("This account or Google identity is already linked. Contact your administrator.");
                alreadyLinked = true;
            }
        }
        if (!alreadyLinked)
        {
            await using var insert = new MySqlCommand("""
                INSERT INTO external_identities(id,user_id,provider,provider_sub) VALUES(@id,@user,'google',@subject)
                """, c, t);
            insert.Parameters.AddWithValue("id", Guid.NewGuid());
            insert.Parameters.AddWithValue("user", userId);
            insert.Parameters.AddWithValue("subject", subject);
            try { await insert.ExecuteNonQueryAsync(cancellationToken); }
            catch (MySqlException ex) when (ex.Number is 1062 or 1213)
            { throw new InvalidOperationException("This Google identity was linked by another request. Sign in again."); }
        }
        await t.CommitAsync(cancellationToken);
    }

    public async Task<ExternalIdentity?> FindAsync(string provider, string providerSub, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT 
                id,
                user_id
            FROM external_identities
            WHERE
                provider = @Provider AND
                provider_sub = @ProviderSub
            """;

        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = new MySqlCommand(sql,connection);
        
        command.AddParam("@Provider", provider);
        command.AddParam("@ProviderSub", providerSub);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        if (await reader.ReadAsync(cancellationToken))
        {
            ExternalIdentity externalIdentity = new(
                reader.GetGuid("id"),
                reader.GetGuid("user_id"),
                provider,
                providerSub);
            
            return externalIdentity;
        }

        return null;
    }

    public async Task<ExternalIdentity?> FindByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        const string sql = """
           SELECT 
               user_id,
               provider,
               provider_sub
           FROM external_identities
           WHERE
               id = @Id
           """;

        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = new MySqlCommand(sql,connection);

        command.AddParam("@Id",id);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        if (await reader.ReadAsync(cancellationToken))
        {
            ExternalIdentity externalIdentity = new(
                id,
                reader.GetGuid("user_id"),
                reader.GetString("provider"),
                reader.GetString("provider_sub"));
            
            return externalIdentity;
        }
        return null;
    }

    public async Task<bool> CreateAsync(ExternalIdentity identity, CancellationToken cancellationToken)
    {
        const string sql = """
           INSERT INTO external_identities
           (
                id,
                user_id,
                provider,
                provider_sub
           )
           VALUES
           (
                @Id,
                @UserId,
                @Provider,
                @ProviderSub
           );
           """;

        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = new MySqlCommand(sql,connection);
        
        command.AddParam("@Id", identity.Id);
        command.AddParam("@UserId", identity.UserId);
        command.AddParam("@Provider", identity.Provider);
        command.AddParam("@ProviderSub", identity.ProviderSub);
        
        var rowsAffected = await command.ExecuteNonQueryAsync(cancellationToken);
        return rowsAffected == 1;
    }
}
