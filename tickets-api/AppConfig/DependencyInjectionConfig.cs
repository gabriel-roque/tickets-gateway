using Microsoft.EntityFrameworkCore;
using TicketsApi.Consumers;
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
        services.AddTransient<IKafkaService, KafkaService>();
        services.AddHttpClient<IGatewayApiService, GatewayApiService>((provider, client) =>
        {
            var configuration = provider.GetRequiredService<IConfiguration>();
            var url = configuration["GatewayApi:Url"]
                ?? throw new InvalidOperationException("GatewayApi:Url is required");
            var apiKey = configuration["GatewayApi:XApiKey"]
                ?? throw new InvalidOperationException("GatewayApi:XApiKey is required");
            client.BaseAddress = new Uri(url, UriKind.Absolute);
            client.DefaultRequestHeaders.Add("x-api-key", apiKey);
            client.Timeout = TimeSpan.FromSeconds(10);
        });

        return services;
    }

    public static IServiceCollection AddRepositoriesDI(this IServiceCollection services)
    {
        services.AddTransient<IEventRepository, EventRepository>();
        services.AddTransient<ITicketRepository, TicketRepository>();

        return services;
    }

    public static IServiceCollection RegisterConsumers(this IServiceCollection services)
    {
        services.AddHostedService<TicketOrderConsumer>();
        services.AddHostedService<PaymentTicketConsumer>();
        
        return services;
    }
}
