using TicketsApi.Models;

namespace TicketsApi.Interfaces.Services;

public interface IEventService
{
    Task<Event> Get(Guid eventId);
    Task<Event> Create(Event @event);
    Task<Event> Update(Event @event);
    Task<IEnumerable<Event>> List(int skip, int take = 10);
}