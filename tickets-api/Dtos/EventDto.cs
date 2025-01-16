using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace TicketsApi.Dtos;

public class EventCreateDto
{
    [JsonPropertyName("name")]
    [MaxLength(20, ErrorMessage = "Max. 20 characters")]
    [MinLength(2, ErrorMessage = "Min. 2 characters")]
    [Required(ErrorMessage = "name is required")]
    public string Name { get; set; }   
    
    [JsonPropertyName("date")]
    [Required(ErrorMessage = "date is required")]
    public DateTime Date { get; set; }    
    
    [JsonPropertyName("total_tickets")]
    [Required(ErrorMessage = "total_tickets is required")]
    public int TotalTickets { get; set; }
}
public class EventUpdateDto : EventCreateDto
{
}