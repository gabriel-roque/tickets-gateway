using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using TicketsApi.Enums;

namespace TicketsApi.Dtos;

public class TicketCreateDto
{
    [JsonPropertyName("event_id")]
    [Required(ErrorMessage = "event_id is required")]
    public Guid EventId { get; set; }
    
    [JsonPropertyName("payment_method")]
    [Required(ErrorMessage = "payment_method is required")]
    public PaymentMethodEnum PaymentMethod { get; set; }
}
