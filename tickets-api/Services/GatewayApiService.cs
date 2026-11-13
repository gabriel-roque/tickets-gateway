using System.Text;
using Newtonsoft.Json;
using TicketsApi.Interfaces.Services;
using TicketsApi.Models;

namespace TicketsApi.Services;

internal sealed record TransactionPixDto(string name, int value, string external_id);

public class GatewayApiService : IGatewayApiService
{
    private readonly HttpClient client;
    private readonly ILogger<GatewayApiService> logger;

    internal HttpClient ConfiguredClient => client;

    public GatewayApiService(HttpClient client, ILogger<GatewayApiService> logger)
    {
        this.client = client;
        this.logger = logger;
    }

    public async Task<bool> CreateTransactionPix(Ticket ticket)
    {
        try
        {
            var transaction = new TransactionPixDto(
                $"{ticket.EventId}-{ticket.OwnerId}-ticket", ticket.Valeu, ticket.Id.ToString());
            
            var content = new StringContent(JsonConvert.SerializeObject(transaction), Encoding.UTF8, "application/json");
            using var response = await client.PostAsync("/transaction-pix", content);
            
            var responseBody = await response.Content.ReadAsStringAsync();
            logger.LogInformation("Gateway response: {Response}", responseBody);

            return response.IsSuccessStatusCode;
        }
        catch (Exception e)
        {
            logger.LogError(e,"FAILED_TRANSACTION_PIX_API");
            return false;
        }
    }
}
