namespace IntegrationEventBus.Infrastructure;

public interface IIntegrationEventSerializer
{
    string Serialize(object value, Type type);

    object Deserialize(string json, Type type);

    string SerializeHeaders(IReadOnlyDictionary<string, string>? headers);

    IReadOnlyDictionary<string, string> DeserializeHeaders(string json);
}
