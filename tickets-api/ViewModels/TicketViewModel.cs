using TicketsApi.Enums;
using TicketsApi.Models;

namespace TicketsApi.ViewModels;

public class TicketViewModel
{
    public Guid Id { get; set; }
    public EventViewModel? Event { get; set; }
    public Guid EventId { get; set; }
    
    public User? Owner { get; set; }
    public Guid OwnerId { get; set; }
    public int Value { get; set; }
    
    public TicketStatusEnum Status { get; set; }
    
    public PaymentMethodEnum PaymentMethod {get; set;}
}