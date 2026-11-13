using EventSphere.Application;
using EventSphere.Infrastructure.Messaging;
using EventSphere.Infrastructure.Persistence;
using EventSphere.Infrastructure.Services;
using EventSphere.Infrastructure.Workers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace EventSphere.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddEventSphere(this IServiceCollection services, IConfiguration configuration)
    {
        var writeConnection = configuration.GetConnectionString("WriteDb");
        var readConnection = configuration.GetConnectionString("ReadDb");

        services.AddDbContext<WriteDbContext>(options =>
        {
            if (string.IsNullOrWhiteSpace(writeConnection)) options.UseInMemoryDatabase("eventsphere-write");
            else options.UseSqlServer(writeConnection);
        });
        services.AddDbContext<ReadDbContext>(options =>
        {
            if (string.IsNullOrWhiteSpace(readConnection)) options.UseInMemoryDatabase("eventsphere-read");
            else options.UseSqlServer(readConnection);
        });

        services.AddSingleton<IClock, SystemClock>();
        services.AddOptions<ReservationOptions>()
            .Bind(configuration.GetSection(ReservationOptions.SectionName))
            .Validate(options => options.TtlMinutes is >= 1 and <= 24 * 60,
                "Reservations:TtlMinutes must be between 1 and 1440.")
            .ValidateOnStart();
        services.AddOptions<SeatLockOptions>()
            .Bind(configuration.GetSection(SeatLockOptions.SectionName))
            .Validate(options => options.TtlSeconds is >= 10 and <= 600,
                "SeatLocks:TtlSeconds must be between 10 and 600.")
            .ValidateOnStart();
        services.AddScoped<IEventStore, SqlEventStore>();
        services.AddScoped<IReadModelStore, SqlReadModelStore>();
        services.AddScoped<ISagaRepository, SqlSagaRepository>();
        services.AddScoped<IInbox, SqlInbox>();
        services.AddScoped<IOrderSubmissionStore, OrderSubmissionStore>();
        services.AddScoped<IIdempotencyStore>(provider => provider.GetRequiredService<IOrderSubmissionStore>() as IIdempotencyStore
            ?? throw new InvalidOperationException("Order submission store must implement idempotency storage."));
        services.AddScoped<AggregateRepository>();
        services.AddScoped<InventoryCommandService>();
        services.AddScoped<OrderCommandService>();
        services.AddScoped<DirectOrderIngress>();
        services.AddScoped<TicketCommandService>();
        services.AddScoped<ProjectionEngine>();
        services.AddScoped<PurchaseSagaOrchestrator>();
        services.AddScoped<IInventoryParticipant, InventoryParticipant>();
        services.AddScoped<IPriceCatalog, PriceCatalog>();
        services.AddScoped<IPricingParticipant, PricingParticipant>();
        services.AddScoped<IFraudParticipant, FraudParticipant>();
        services.AddScoped<IPaymentParticipant, PaymentParticipant>();
        services.AddScoped<ITicketParticipant, TicketParticipant>();
        services.AddScoped<ISettlementParticipant, SettlementParticipant>();
        services.AddScoped<INotificationParticipant, NotificationParticipant>();
        services.AddScoped<ILifecycleWorkflowRepository, SqlLifecycleWorkflowRepository>();
        services.AddScoped<LifecycleSagaService>();
        services.AddScoped<ISeatLockService, SqlSeatLockService>();

        var kafkaServers = configuration["Kafka:BootstrapServers"];
        var asyncIngress = configuration.GetValue("Commands:AsyncIngress", false);
        if (asyncIngress)
        {
            if (string.IsNullOrWhiteSpace(kafkaServers))
                throw new InvalidOperationException("Kafka:BootstrapServers is required for asynchronous command ingress.");
            services.AddSingleton<IOrderIngress>(_ => new KafkaOrderIngress(kafkaServers));
        }
        else
            services.AddScoped<IOrderIngress>(provider => provider.GetRequiredService<DirectOrderIngress>());
        if (string.IsNullOrWhiteSpace(kafkaServers))
            services.AddSingleton<IMessagePublisher, LoggingMessagePublisher>();
        else
            services.AddSingleton<IMessagePublisher>(_ => new KafkaMessagePublisher(kafkaServers));
        services.AddSingleton<OutboxDispatcher>();
        return services;
    }
}
