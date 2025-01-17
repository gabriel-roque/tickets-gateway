using TicketsApi.Interfaces.Repositories;
using TicketsApi.Interfaces.Services;
using TicketsApi.Models;

namespace TicketsApi.Services;

public class TicketService (
        ITicketRepository ticketRepository
    )
    : ITicketService
{
    public async Task<Ticket> Get(Guid ticketId) => await ticketRepository.Get(ticketId);
    public async Task<Ticket> Create(Ticket ticket) => await ticketRepository.Create(ticket);
    public async Task<Ticket> Update(Ticket ticket) => await ticketRepository.Update(ticket);
    public Task<IEnumerable<Ticket>> ListByOwner(Guid ownerId, int skip, int take = 10)
        => ticketRepository.ListByOwner(ownerId, skip, take);
}