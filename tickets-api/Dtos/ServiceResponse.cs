namespace TicketsApi.Dtos;

public class ServiceResponse
{
    public record CreateAccount(string message);
    public record LoginResponse(string token, string message);
}