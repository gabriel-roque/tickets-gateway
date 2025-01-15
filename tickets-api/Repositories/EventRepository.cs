using Microsoft.EntityFrameworkCore;
using TicketsApi.AppConfig;
using TicketsApi.AppConfig.Errors;
using TicketsApi.Interfaces.Repositories;
using TicketsApi.Models;

namespace TicketsApi.Repositories;

public class EventRepository(
    AppDbContext database
    ): IEventRepository
{
    
    public async Task<Event> Get(Guid eventId)
    {
        var @event = await database.Event
                .AsNoTracking()
                .FirstOrDefaultAsync(@event => @event.Id == eventId);

        return @event;
    }

    public async Task<Event> Create(Event @event)
    {
        database.Event.Add(@event);

        await database.SaveChangesAsync();
        
        return @event;
    }

    public async Task<Event> Update(Event @event)
    {
        var eventFound =
            await database.Event
                .FirstOrDefaultAsync((r) =>
                    r.Id == @event.Id);

        if (eventFound is null)
            throw new NotFoundException("Event not found with version");

        eventFound.Name = @event.Name;
        eventFound.Date = @event.Date;
        eventFound.TotalTickets = @event.TotalTickets;

        database.Update(eventFound);
        
        await database.SaveChangesAsync();

        return eventFound;
    }

    public async Task<IEnumerable<Event>> List(int skip, int take = 10)
    {
        var events = await database.Event.Skip(skip).Take(take).AsNoTracking().ToListAsync();

        return events;
    }
}