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
        database.Update(@event);
        await database.SaveChangesAsync();

        return await Get(@event.Id);;
    }

    public async Task<IEnumerable<Event>> List(int skip, int take = 10)
    {
        var events = await database.Event.Skip(skip).Take(take).AsNoTracking().ToListAsync();

        return events;
    }
}