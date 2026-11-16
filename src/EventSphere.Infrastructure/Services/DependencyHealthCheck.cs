using Confluent.Kafka;
using EventSphere.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace EventSphere.Infrastructure.Services;

public sealed class DependencyHealthCheck(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var write = scope.ServiceProvider.GetRequiredService<WriteDbContext>();
            var read = scope.ServiceProvider.GetRequiredService<ReadDbContext>();
            if (write.Database.IsRelational() && !await write.Database.CanConnectAsync(cancellationToken))
                return HealthCheckResult.Unhealthy("Write database is unavailable.");
            if (read.Database.IsRelational() && !await read.Database.CanConnectAsync(cancellationToken))
                return HealthCheckResult.Unhealthy("Read database is unavailable.");

            var kafka = configuration["Kafka:BootstrapServers"];
            if (!string.IsNullOrWhiteSpace(kafka))
            {
                using var admin = new AdminClientBuilder(new AdminClientConfig { BootstrapServers = kafka }).Build();
                admin.GetMetadata(TimeSpan.FromSeconds(2));
            }
            return HealthCheckResult.Healthy();
        }
        catch (Exception exception)
        {
            return HealthCheckResult.Unhealthy("A required dependency is unavailable.", exception);
        }
    }
}
