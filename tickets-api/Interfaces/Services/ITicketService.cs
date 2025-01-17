using TicketsApi.Models;

namespace TicketsApi.Interfaces.Services;

public interface ITicketService
{
    Task<Ticket> Get(Guid ticketId);
    Task<Ticket> Create(Ticket ticket);
    Task<Ticket> Update(Ticket ticket);
    Task<IEnumerable<Ticket>> ListByOwner(Guid ownerId, int skip, int take = 10);
}