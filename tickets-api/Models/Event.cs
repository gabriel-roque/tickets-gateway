using System.ComponentModel.DataAnnotations.Schema;
using TicketsApi.ViewModels;

namespace TicketsApi.Models;

[Table("Events")]
public class Event : Entity, IView<EventViewModel, Event>
{
    public string Name { get; set; }
    public DateTime Date { get; set; }
    public int TotalTickets { get; set; } = 0;
    public int PriceTicket { get; set; } = 0;
    
    public User Owner { get; set; }
    public Guid OwnerId { get; set; }

    public static EventViewModel ToGetView(Event model)
    {
        return new EventViewModel()
        {
            Id = model.Id,
            Name = model.Name,
            Date = model.Date,
            TotalTickets = model.TotalTickets,
            PriceTicket = model.PriceTicket
        };
    }

    public static List<EventViewModel> ToListView(IEnumerable<Event> items)
        => items.Select(ToGetView).ToList();
}

