namespace IntegrationEventBus.Infrastructure;

public interface IIntegrationEventStoreInitializer
{
    Task InitializeAsync(CancellationToken cancellationToken);
}
