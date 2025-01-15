namespace TicketsApi.Models;

public class Entity
{
    public Guid Id { get; set; }
    public DateTimeOffset CreationDate { get; set; }
    public DateTimeOffset UpdateDate { get; set; }
    public bool Deleted { get; private set; }
    public Guid? DeletedBy { get; private set; }
    public DateTimeOffset? DeleteDate { get; private set; }

    public void Delete(Guid? userId = null)
    {
        Deleted = true;
        DeletedBy = userId;
        DeleteDate = DateTimeOffset.UtcNow;
    }
}