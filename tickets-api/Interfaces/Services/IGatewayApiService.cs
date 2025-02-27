using TicketsApi.Models;

namespace TicketsApi.Interfaces.Services;

public interface IGatewayApiService
{
    Task<bool> CreateTransactionPix(Ticket ticket);
}