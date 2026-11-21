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
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new MySqlCommand("CALL sp_external_identity_link_google(@p0, @p1, @p2, @p3);", connection);
        command.Parameters.AddWithValue("p0", userId);
        command.Parameters.AddWithValue("p1", email.Trim().ToLowerInvariant());
        command.Parameters.AddWithValue("p2", subject);
        command.Parameters.AddWithValue("p3", expectedPasswordHash);
        try { await command.ExecuteAffectedRowsAsync(cancellationToken); }
        catch (MySqlException ex) when (ex.Number is 1062 or 1213)
        { throw new InvalidOperationException("This Google identity was linked by another request. Sign in again.", ex); }
        catch (MySqlException ex) when (ex.Number == 1644)
        { throw new InvalidOperationException(ex.Message, ex); }
    }

    public async Task<ExternalIdentity?> FindAsync(string provider, string providerSub, CancellationToken cancellationToken)
    {
        const string sql = "CALL sp_external_identity_find(@Provider, @ProviderSub);";

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
        const string sql = "CALL sp_external_identity_find_by_id(@Id);";

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
        const string sql = "CALL sp_external_identity_create(@Id, @UserId, @Provider, @ProviderSub);";

        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = new MySqlCommand(sql,connection);
        
        command.AddParam("@Id", identity.Id);
        command.AddParam("@UserId", identity.UserId);
        command.AddParam("@Provider", identity.Provider);
        command.AddParam("@ProviderSub", identity.ProviderSub);
        
        var rowsAffected = await command.ExecuteAffectedRowsAsync(cancellationToken);
        return rowsAffected == 1;
    }
}
