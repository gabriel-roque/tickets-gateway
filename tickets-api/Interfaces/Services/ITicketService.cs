using TicketsApi.Models;

namespace TicketsApi.Interfaces.Services;

public interface ITicketService
{
    Task<Ticket> Get(Guid ticketId);
    Task<Ticket> Create(Ticket ticket);
    Task<Ticket> Update(Ticket ticket);
}