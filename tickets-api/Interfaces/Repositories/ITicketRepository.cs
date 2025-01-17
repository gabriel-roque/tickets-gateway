using TicketsApi.Models;

namespace TicketsApi.Interfaces.Repositories;

public interface ITicketRepository
{
    Task<Ticket> Get(Guid ticketId);
    Task<Ticket> Create(Ticket ticket);
    Task<Ticket> Update(Ticket ticket);
}