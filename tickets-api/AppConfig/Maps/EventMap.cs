using Microsoft.EntityFrameworkCore;
using TicketsApi.Models;

namespace TicketsApi.AppConfig.Maps;

internal static class EventMap
{
    public static void Map(ModelBuilder modelBuilder)
    {
        modelBuilder.Map<Event>();
        modelBuilder.Entity<Event>(builder =>
        {
            builder.Property(p => p.Name).DefaultString(20, true);
            builder.Property(p => p.Date);
            builder.Property(p => p.TotalTickets);
        });
    }
}