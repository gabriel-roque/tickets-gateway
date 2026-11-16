using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace EventSphere.Application;

public static class EventSphereTelemetry
{
    public const string Name = "EventSphere";
    public static readonly ActivitySource Activities = new(Name);
    public static readonly Meter Meter = new(Name);
    public static readonly Counter<long> OrdersAccepted = Meter.CreateCounter<long>("eventsphere.orders.accepted");
    public static readonly Counter<long> SagaTransitions = Meter.CreateCounter<long>("eventsphere.saga.transitions");
    public static readonly Counter<long> SagaCompensations = Meter.CreateCounter<long>("eventsphere.saga.compensations");
    public static readonly Counter<long> OutboxPublished = Meter.CreateCounter<long>("eventsphere.outbox.published");
    public static readonly Counter<long> OutboxFailures = Meter.CreateCounter<long>("eventsphere.outbox.failures");
    public static readonly Histogram<double> CommandDuration = Meter.CreateHistogram<double>("eventsphere.command.duration", "ms");
}
