namespace IntegrationEventBus.Infrastructure;

public interface IIntegrationEventDispatcher
{
    ValueTask DispatchAsync(
        StoredEventDelivery delivery,
        SubscriptionDefinition subscription,
        CancellationToken cancellationToken);
}
