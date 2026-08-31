using System.Text.Json;
using IntegrationEventBus.Infrastructure;

namespace IntegrationEventBus;

internal sealed class SystemTextJsonIntegrationEventSerializer(JsonSerializerOptions options)
    : IIntegrationEventSerializer
{
    public string Serialize(object value, Type type) =>
        JsonSerializer.Serialize(value, type, options);

    public object Deserialize(string json, Type type) =>
        JsonSerializer.Deserialize(json, type, options)
        ?? throw new JsonException($"The payload for event type '{type.FullName}' deserialized to null.");
}
