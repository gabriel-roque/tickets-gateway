using Microsoft.EntityFrameworkCore;
using TicketsApi.Interfaces.Repositories;
using TicketsApi.Interfaces.Services;
using TicketsApi.Repositories;
using TicketsApi.Services;

namespace TicketsApi.AppConfig;

public static class DependencyInjectionConfig
{
    public static IServiceCollection AddDatabasesConfigDI(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        services.AddDbContext<AppDbContext>(options =>
        {
            options.UseSqlServer(configuration.GetConnectionString("TicketDB") ??
                throw new InvalidOperationException("Connection String is not found"));
        });

        return services;
    }

    public static IServiceCollection AddApplicationServicesDI(this IServiceCollection services)
    {
        services.AddTransient<ITokenService, TokenService>();
        services.AddTransient<IUserAccountService, UserAccountService>();
        services.AddTransient<IEventService, EventService>();
        services.AddTransient<ITicketService, TicketService>();

        return services;
    }

    public static IServiceCollection AddRepositoriesDI(this IServiceCollection services)
    {
        services.AddTransient<IEventRepository, EventRepository>();
        services.AddTransient<ITicketRepository, TicketRepository>();

        return services;
    }
}