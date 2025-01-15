using TicketsApi.Dtos;

namespace TicketsApi.Interfaces.Services;

public interface ITokenService
{
    string Generate (UserSession user);
}