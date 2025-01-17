namespace TicketsApi.ViewModels;

public class EventViewModel
{
    public Guid Id { get; set; }
    public string Name { get; set; }
    public DateTime Date { get; set; }
    public int TotalTickets { get; set; }
    public int PriceTicket { get; set; }
}