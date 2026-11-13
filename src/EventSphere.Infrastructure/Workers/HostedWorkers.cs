using EventSphere.Application;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace EventSphere.Infrastructure.Workers;

public sealed class SagaWorker(IServiceScopeFactory scopeFactory, ILogger<SagaWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                IReadOnlyList<PurchaseSagaState> candidates;
                await using (var scope = scopeFactory.CreateAsyncScope())
                {
                    var repository = scope.ServiceProvider.GetRequiredService<ISagaRepository>();
                    candidates = await repository.ClaimRunnableAsync(50, TimeSpan.FromSeconds(30), stoppingToken);
                }

                foreach (var candidate in candidates)
                {
                    await using var scope = scopeFactory.CreateAsyncScope();
                    var orchestrator = scope.ServiceProvider.GetRequiredService<PurchaseSagaOrchestrator>();
                    var repository = scope.ServiceProvider.GetRequiredService<ISagaRepository>();
                    var clock = scope.ServiceProvider.GetRequiredService<IClock>();
                    try
                    {
                        var current = candidate;
                        for (var transition = 0; transition < 12; transition++)
                        {
                            if (current.Status == SagaStatus.Compensating)
                                await orchestrator.CompensateAsync(current.Id, stoppingToken);
                            else
                                await orchestrator.ProcessAsync(current.Id, stoppingToken);
                            current = await repository.GetAsync(current.Id, stoppingToken) ?? current;
                            if (current.Status is SagaStatus.Completed or SagaStatus.Compensated or SagaStatus.ManualIntervention)
                                break;
                            if (current.LeaseUntil is null || current.NextAttemptAt > clock.UtcNow)
                                break;
                        }
                    }
                    catch (Exception exception) when (exception is not OperationCanceledException)
                    {
                        logger.LogWarning(exception, "Saga {SagaId} will be retried after its lease expires", candidate.Id);
                    }
                }
                if (candidates.Count == 0) await Task.Delay(100, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
            catch (Exception exception)
            {
                logger.LogError(exception, "Saga worker iteration failed");
                await Task.Delay(1000, stoppingToken);
            }
        }
    }
}

public sealed class ProjectionWorker(IServiceScopeFactory scopeFactory, ILogger<ProjectionWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var engine = scope.ServiceProvider.GetRequiredService<ProjectionEngine>();
                var count = await engine.RunBatchAsync(ProjectionEngine.MemoryBoundBatchSize, stoppingToken);
                if (count == 0) await Task.Delay(100, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
            catch (Exception exception)
            {
                logger.LogError(exception, "Projection worker iteration failed");
                await Task.Delay(1000, stoppingToken);
            }
        }
    }
}

public sealed class OutboxWorker(OutboxDispatcher dispatcher, ILogger<OutboxWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var count = await dispatcher.RunBatchAsync(200, stoppingToken);
                if (count == 0) await Task.Delay(200, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
            catch (Exception exception)
            {
                logger.LogError(exception, "Outbox worker iteration failed");
                await Task.Delay(1000, stoppingToken);
            }
        }
    }
}

public sealed class LifecycleWorkflowWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<LifecycleWorkflowWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                IReadOnlyList<LifecycleWorkflowState> candidates;
                await using (var scope = scopeFactory.CreateAsyncScope())
                {
                    candidates = await scope.ServiceProvider.GetRequiredService<ILifecycleWorkflowRepository>()
                        .ClaimRunnableAsync(50, TimeSpan.FromSeconds(30), stoppingToken);
                }
                foreach (var candidate in candidates)
                {
                    await using var scope = scopeFactory.CreateAsyncScope();
                    await scope.ServiceProvider.GetRequiredService<LifecycleSagaService>()
                        .ProcessAsync(candidate.Id, stoppingToken);
                }
                if (candidates.Count == 0) await Task.Delay(200, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
            catch (Exception exception)
            {
                logger.LogError(exception, "Lifecycle workflow worker iteration failed");
                await Task.Delay(1000, stoppingToken);
            }
        }
    }
}

public sealed class ReservationExpiryWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<ReservationExpiryWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var inventory = scope.ServiceProvider.GetRequiredService<IInventoryParticipant>();
                var count = await inventory.ExpireDueAsync(100, stoppingToken);
                if (count == 0) await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
            catch (Exception exception)
            {
                logger.LogError(exception, "Reservation expiry iteration failed");
                await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
            }
        }
    }
}
