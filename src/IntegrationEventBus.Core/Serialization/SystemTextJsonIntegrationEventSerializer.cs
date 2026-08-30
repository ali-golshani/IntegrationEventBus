using System.Text.Json;
using IntegrationEventBus.Infrastructure;

namespace IntegrationEventBus;

internal sealed class SystemTextJsonIntegrationEventSerializer(JsonSerializerOptions options)
    : IIntegrationEventSerializer
{
    private static readonly IReadOnlyDictionary<string, string> EmptyHeaders =
        new Dictionary<string, string>();

    public string Serialize(object value, Type type) =>
        JsonSerializer.Serialize(value, type, options);

    public object Deserialize(string json, Type type) =>
        JsonSerializer.Deserialize(json, type, options)
        ?? throw new JsonException($"The payload for event type '{type.FullName}' deserialized to null.");

    public string SerializeHeaders(IReadOnlyDictionary<string, string>? headers) =>
        headers is null || headers.Count == 0
            ? "{}"
            : JsonSerializer.Serialize(headers, options);

    public IReadOnlyDictionary<string, string> DeserializeHeaders(string json) =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(json, options)
        ?? EmptyHeaders;
}
