namespace CLMS.Domain.Entities.Authentication;

public class ExternalIdentity
{
    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public string Provider { get; private set; }
    public string ProviderUserId { get; private set; }

    public ExternalIdentity(Guid id, Guid userId, string provider, string providerUserId)
    {
        Id = id;
        UserId = userId;
        Provider = provider;
        ProviderUserId = providerUserId;
    }

}
