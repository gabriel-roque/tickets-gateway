using System.Text.Json;
using EventSphere.Application;
using EventSphere.Domain;

namespace EventSphere.Infrastructure.Persistence;

public static class EventSerializer
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
    private static readonly IReadOnlyDictionary<string, Type> EventTypes = typeof(IDomainEvent).Assembly
        .GetTypes()
        .Where(type => typeof(IDomainEvent).IsAssignableFrom(type) && type is { IsAbstract: false, IsInterface: false })
        .ToDictionary(type => type.Name, StringComparer.Ordinal);

    public static string Serialize(IDomainEvent @event) => JsonSerializer.Serialize(@event, @event.GetType(), Options);
    public static string SerializeMetadata(EventMetadata metadata) => JsonSerializer.Serialize(metadata, Options);
    public static IDomainEvent Deserialize(string type, int schemaVersion, string payload)
    {
        if (schemaVersion != 1)
            throw new InvalidOperationException($"Unsupported schema version {schemaVersion} for '{type}'. Register an upcaster before replay.");
        if (!EventTypes.TryGetValue(type, out var eventType))
            throw new InvalidOperationException($"Unsupported event type '{type}'. Add an upcaster before deploying this schema.");
        return (IDomainEvent)(JsonSerializer.Deserialize(payload, eventType, Options)
            ?? throw new InvalidOperationException($"Event '{type}' could not be deserialized."));
    }
    public static EventMetadata DeserializeMetadata(string payload) =>
        JsonSerializer.Deserialize<EventMetadata>(payload, Options)
        ?? throw new InvalidOperationException("Event metadata could not be deserialized.");
}
