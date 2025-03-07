using System.Text;
using System.Text.Json.Serialization;
using Microsoft.OpenApi.Extensions;
using Newtonsoft.Json;
using TicketsApi.Interfaces.Services;
using TicketsApi.Models;

namespace TicketsApi.Services;

class TransactonPixDTO
{
    public string name {get; set;}
    public int value {get; set;}
    public string external_id {get; set;}
} 

public class GatewayApiService : IGatewayApiService
{
    HttpClient client = new HttpClient();
    private readonly IConfiguration _config;
    private readonly ILogger<GatewayApiService> _logger;
    
    public GatewayApiService(IConfiguration config, ILogger<GatewayApiService> logger)
    {
        _config = config;
        client.BaseAddress = new Uri(_config["GatewayApi:Url"]);
        client.DefaultRequestHeaders.Add("x-api-key", _config["GatewayApi:XApiKey"]);
    }
    
    public Task<bool> CreateTransactionPix(Ticket ticket)
    {
        try
        {
            var transaction = new TransactonPixDTO()
            {
                name = $"{ticket.EventId}-{ticket.OwnerId}-ticket",
                value = ticket.Valeu,
                external_id = ticket.Id.ToString()
            };
            
            var content = new StringContent(JsonConvert.SerializeObject(transaction), Encoding.UTF8, "application/json");
            var response = client.PostAsync("/transaction-pix", content).Result;
            
            _logger.LogInformation(JsonConvert.DeserializeObject(response.Content.ReadAsStringAsync().Result).ToString());

            return Task.FromResult(response.IsSuccessStatusCode);
        }
        catch (Exception e)
        {
            _logger.LogError(e,"FAILED_TRANSACTION_PIX_API");
            return Task.FromResult(false);
        }
    }
}