using Microsoft.AspNetCore.Identity;

namespace TicketsApi.Models;

public class User : IdentityUser
{
    public string? Name { get; set; }
}