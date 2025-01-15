using TicketsApi.Interfaces.Repositories;
using TicketsApi.Interfaces.Services;
using TicketsApi.Models;

namespace TicketsApi.Services;

public class EventService (
        IEventRepository eventRepository
    )
    : IEventService
{
    public async Task<Event> Get(Guid eventId) => await eventRepository.Get(eventId);
    public async Task<Event> Create(Event @event) => await eventRepository.Create(@event);
    public async Task<Event> Update(Event @event) => await eventRepository.Update(@event);
    public async Task<IEnumerable<Event>> List(int skip, int take = 10) => await eventRepository.List(skip, take);
}