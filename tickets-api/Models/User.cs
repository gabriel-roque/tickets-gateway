using Microsoft.AspNetCore.Identity;

namespace TicketsApi.Models;

public class User : IdentityUser<Guid>
{
    public string? Name { get; set; }
}