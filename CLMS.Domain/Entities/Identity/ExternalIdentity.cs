namespace CLMS.Domain.Entities.Identity;

public class ExternalIdentity
{
    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public string Provider { get; private set; }
    public string ProviderSub { get; private set; }

    public ExternalIdentity(Guid id, Guid userId, string provider, string providerSub)
    {
        Id = id;
        UserId = userId;
        Provider = provider;
        ProviderSub = providerSub;
    }

}
