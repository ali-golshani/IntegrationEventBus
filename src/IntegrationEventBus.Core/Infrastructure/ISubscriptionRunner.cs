namespace IntegrationEventBus.Infrastructure;

public interface ISubscriptionRunner
{
    Task RunAsync(
        SubscriptionDefinition subscription,
        string processorId,
        CancellationToken cancellationToken);
}
