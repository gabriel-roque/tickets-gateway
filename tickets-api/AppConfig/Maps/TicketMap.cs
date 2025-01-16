using Microsoft.EntityFrameworkCore;
using TicketsApi.Enums;
using TicketsApi.Models;

namespace TicketsApi.AppConfig.Maps;

internal static class TicketMap
{
    public static void Map(ModelBuilder modelBuilder)
    {
        modelBuilder.Map<Ticket>();
        modelBuilder.Entity<Ticket>(builder => { });
    }
}