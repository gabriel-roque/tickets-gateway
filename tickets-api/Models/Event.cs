using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TicketsApi.Models;

[Table("Events")]
public class Event : Entity
{
    public string Name { get; set; }
    public DateTime Date { get; set; }
    public int TotalTickets { get; set; } = 0;
    
    [ConcurrencyCheck]
    public long Version { get; set; } = 0;
}