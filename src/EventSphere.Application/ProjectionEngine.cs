namespace EventSphere.Application;

public sealed class ProjectionEngine(IEventStore eventStore, IReadModelStore readModels)
{
    public const string ProjectorName = "eventsphere-v1";
    public const int MemoryBoundBatchSize = 100;

    public async Task<int> RunBatchAsync(int batchSize, CancellationToken cancellationToken)
    {
        var checkpoint = await readModels.GetCheckpointAsync(ProjectorName, cancellationToken);
        var events = await eventStore.ReadAllAsync(checkpoint, batchSize, cancellationToken);
        await readModels.ProjectBatchAsync(ProjectorName, events, cancellationToken);
        return events.Count;
    }

    public async Task RebuildAsync(CancellationToken cancellationToken)
    {
        await readModels.ResetAsync(ProjectorName, cancellationToken);
        while (await RunBatchAsync(MemoryBoundBatchSize, cancellationToken) > 0) { }
    }
}
