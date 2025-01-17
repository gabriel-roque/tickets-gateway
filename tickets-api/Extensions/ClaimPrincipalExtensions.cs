using System.Security.Claims;

namespace TicketsApi;

public static class ClaimPrincipalExtensions
{
    public static Guid GetUserId(this ClaimsPrincipal user)
    {
        var id = user?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return id == null ? Guid.Empty : Guid.Parse(id);
    }
}
