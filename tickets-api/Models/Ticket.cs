using System.ComponentModel.DataAnnotations.Schema;
using TicketsApi.Enums;

namespace TicketsApi.Models;

[Table("Tickets")]
public class Ticket : Entity
{
    public Event Event { get; set; }
    public Guid EventId { get; set; }
    
    public User User { get; set; }
    public Guid UserId { get; set; }

    public TicketStatusEnum Status { get; set; } = TicketStatusEnum.Pending;
}