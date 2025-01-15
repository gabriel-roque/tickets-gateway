using TicketsApi.Models;

namespace TicketsApi.Interfaces.Repositories;

public interface IEventRepository
{
    Task<Event> Get(Guid eventId);
    Task<Event> Create(Event @event);
    Task<Event> Update(Event @event);
    Task<IEnumerable<Event>> List(int skip, int take = 10);
}