using System.Text.Json;
using TicketsApi.Enums;
using TicketsApi.Interfaces.Repositories;
using TicketsApi.Interfaces.Services;
using TicketsApi.Models;

namespace TicketsApi.Services;

public class TicketService (
        IKafkaService kafkaService,
        ITicketRepository ticketRepository,
        IEventRepository eventRepository
    )
    : ITicketService
{
    public async Task<Ticket> Get(Guid ticketId) => await ticketRepository.Get(ticketId);

    public async Task<Ticket> Create(Ticket ticket)
    {
        var _event = await eventRepository.Get(ticket.EventId);
        ticket.Valeu = _event.PriceTicket;
        
        await kafkaService.SendMessageAsync<Ticket>(KafkaTopicsEnum.TicketOrder, JsonSerializer.Serialize(ticket));
        
        return ticket;
    }
    public async Task<Ticket> Update(Ticket ticket) => await ticketRepository.Update(ticket);
    public Task<IEnumerable<Ticket>> ListByOwner(Guid ownerId, int skip, int take = 10)
        => ticketRepository.ListByOwner(ownerId, skip, take);
}