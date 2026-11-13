using Microsoft.EntityFrameworkCore;
using TicketsApi.AppConfig;
using TicketsApi.Interfaces.Repositories;
using TicketsApi.Models;

namespace TicketsApi.Repositories;

public class TicketRepository(
    AppDbContext database
    ): ITicketRepository
{
    
    public async Task<Ticket> Get(Guid ticketId)
    {
        var ticket = await database.Ticket
                .AsNoTracking()
                .FirstOrDefaultAsync(ticket => ticket.Id == ticketId);

        return ticket;
    }

    public async Task<Ticket> Create(Ticket ticket)
    {
        database.Ticket.Add(ticket);

        await database.SaveChangesAsync();
        
        return ticket;
    }

    public async Task BulkCreate(List<Ticket> tickets)
    {
        var uniqueTickets = tickets.GroupBy(ticket => ticket.Id).Select(group => group.Last()).ToList();
        var ids = uniqueTickets.Select(ticket => ticket.Id).ToArray();
        var existing = await database.Ticket.AsNoTracking()
            .Where(ticket => ids.Contains(ticket.Id))
            .Select(ticket => ticket.Id)
            .ToListAsync();
        var existingIds = existing.ToHashSet();
        database.Ticket.AddRange(uniqueTickets.Where(ticket => !existingIds.Contains(ticket.Id)));
        await database.SaveChangesAsync();
    }

    public async Task<Ticket> Update(Ticket ticket)
    {
        database.Update(ticket);
        await database.SaveChangesAsync();

        return await Get(ticket.Id);;
    }

    public async Task<IEnumerable<Ticket>> ListByOwner(Guid ownerId, int skip, int take = 10)
    {
        var tickets = await database.Ticket
            .Where(ticket => ticket.OwnerId == ownerId)
            .Skip(skip).Take(take)
            .AsNoTracking().ToListAsync();

        return tickets;
    }
}
