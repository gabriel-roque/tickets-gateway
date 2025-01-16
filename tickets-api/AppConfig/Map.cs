using Microsoft.EntityFrameworkCore;
using TicketsApi.AppConfig.Maps;

namespace TicketsApi.AppConfig;

public static class Map
{
    public static void Setup(ModelBuilder builder)
    {
        EventMap.Map(builder);
        TicketMap.Map(builder);
    }
}