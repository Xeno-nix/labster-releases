using CLMS.Application.Persistence.Interfaces;
using CLMS.Domain.Entities.Identity;
using CLMS.Infrastructure.Persistence.MySql.Utils;

using MySqlConnector;

namespace CLMS.Infrastructure.Persistence.MySql.Repositories;

public class ExternalIdentityRepository(MySqlConnectionFactory connectionFactory) : IExternalIdentityRepository
{
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
