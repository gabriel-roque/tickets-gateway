using System.ComponentModel.DataAnnotations.Schema;
using TicketsApi.Enums;
using TicketsApi.ViewModels;

namespace TicketsApi.Models;

[Table("Tickets")]
public class Ticket : Entity, IView<TicketViewModel, Ticket>
{
    public Event? Event { get; set; }
    public Guid EventId { get; set; }
    
    public User? Owner { get; set; }
    public Guid OwnerId { get; set; }
    public int Valeu { get; set; }

    public TicketStatusEnum Status { get; set; } = TicketStatusEnum.Pending;
    public PaymentMethodEnum PaymentMethod { get; set; }

    public static TicketViewModel ToGetView(Ticket model)
    {
        return new TicketViewModel()
        {
            Id = model.Id,
            Event = model.Event is not null ? Event.ToGetView(model.Event) : null,
            EventId = model.EventId,
            OwnerId = model.OwnerId,
            Value = model.Valeu
        };
    }

    public static List<TicketViewModel> ToListView(IEnumerable<Ticket> items) 
        => items.Select(ToGetView).ToList();
}