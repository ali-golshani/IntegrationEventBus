namespace IntegrationEventBus.Infrastructure;

public interface IIntegrationEventSerializer
{
    string Serialize(object value, Type type);

    object Deserialize(string json, Type type);
}
