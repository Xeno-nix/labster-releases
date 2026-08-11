namespace CLMS.Domain.Entities.Booking;
public class Laboratory
{
    public readonly int Id;
    public string Name { get; set; }

    public Laboratory(int id, string name)
    {
        Id = id;
        Name = name;
    }
}
