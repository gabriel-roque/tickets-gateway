using System.ComponentModel.DataAnnotations;

namespace TicketsApi.Models;

public class Event : Entity
{
    public string Name { get; set; }
    public DateTime Date { get; set; }
    public int TotalTickets { get; set; } = 0;
    
    [ConcurrencyCheck]
    public long Version { get; set; } = 0;
}