using TicketsApi.Enums;
using TicketsApi.Models;

namespace TicketsApi.ViewModels;

public class TicketViewModel
{
    public EventViewModel? Event { get; set; }
    public Guid EventId { get; set; }
    
    public User? Owner { get; set; }
    public Guid OwnerId { get; set; }
    
    public TicketStatusEnum Status { get; set; }
}