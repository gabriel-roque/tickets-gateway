using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TicketsApi.Models;

public class EventViewModel
{
    public Guid Id { get; set; }
    public string Name { get; set; }
    public DateTime Date { get; set; }
    public int TotalTickets { get; set; }
}

[Table("Events")]
public class Event : Entity
{
    public string Name { get; set; }
    public DateTime Date { get; set; }
    public int TotalTickets { get; set; } = 0;
    
    public User Owner { get; set; }
    public Guid OwnerId { get; set; }
    
    [ConcurrencyCheck]
    public long Version { get; set; } = 0;

    public EventViewModel ToGetView()
    {
        return new EventViewModel()
        {
            Id = Id,
            Name = Name,
            Date = Date,
            TotalTickets = TotalTickets
        };
    }
    
    public static List<EventViewModel> ToListView(IEnumerable<Event> events)
    {
      var items = new List<EventViewModel>();

      foreach (var @event in events)
          items.Add(@event.ToGetView());
      
      return items;
    }
}

