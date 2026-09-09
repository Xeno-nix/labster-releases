namespace CLMS.Domain.Common;

public abstract class AuditableEntity
{
    public Guid _id { get; set; }
    public Guid Id { get { return _id; } }
    public DateTime CreatedAt { get; set; }
    public DateTime LastModifiedAt { get; set; }
    public DateTime ArchivedAt { get; set;}
}
