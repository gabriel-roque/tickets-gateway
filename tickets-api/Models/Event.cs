using System.ComponentModel.DataAnnotations.Schema;

namespace TicketsApi.Models;

public class EventViewModel
{
    public Guid Id { get; set; }
    public string Name { get; set; }
    public DateTime Date { get; set; }
    public int TotalTickets { get; set; }
    public int PriceTicket { get; set; }
}

[Table("Events")]
public class Event : Entity
{
    public string Name { get; set; }
    public DateTime Date { get; set; }
    public int TotalTickets { get; set; } = 0;
    public int PriceTicket { get; set; } = 0;
    
    public User Owner { get; set; }
    public Guid OwnerId { get; set; }

    public EventViewModel ToGetView()
    {
        return new EventViewModel()
        {
            Id = Id,
            Name = Name,
            Date = Date,
            TotalTickets = TotalTickets,
            PriceTicket = PriceTicket
        };
    }
    
    public static List<EventViewModel> ToListView(IEnumerable<Event> events)
    {
        return events.Select(@event => @event.ToGetView()).ToList();
    }
}

