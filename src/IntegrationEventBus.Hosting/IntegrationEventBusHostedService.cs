using IntegrationEventBus.Infrastructure;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace IntegrationEventBus;

internal sealed class IntegrationEventBusHostedService(
    IntegrationEventTopology topology,
    IIntegrationEventStoreInitializer storeInitializer,
    ISubscriptionRunner subscriptionRunner,
    ILogger<IntegrationEventBusHostedService> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var subscriptions = topology.Subscriptions
            .Where(static subscription => subscription.HasHandlers)
            .ToArray();

        ValidateHandlers(subscriptions);
        await storeInitializer.InitializeAsync(stoppingToken).ConfigureAwait(false);

        if (subscriptions.Length == 0)
        {
            logger.LogInformation("IntegrationEventBus processor has no local subscriptions to run.");
            await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken).ConfigureAwait(false);
            return;
        }

        var processorId = CreateProcessorId();
        logger.LogInformation(
            "IntegrationEventBus processor {ProcessorId} is starting {SubscriptionCount} subscription loops.",
            processorId,
            subscriptions.Length);

        var loops = subscriptions
            .Select(subscription => subscriptionRunner.RunAsync(
                subscription,
                processorId,
                stoppingToken))
            .ToArray();

        await Task.WhenAll(loops).ConfigureAwait(false);
    }

    private static void ValidateHandlers(IEnumerable<SubscriptionDefinition> subscriptions)
    {
        foreach (var subscription in subscriptions)
        {
            var missingHandlers = subscription.Routes
                .Where(static route => route.Value is null)
                .Select(static route => route.Key.FullName ?? route.Key.Name)
                .ToArray();

            if (missingHandlers.Length > 0)
            {
                throw new InvalidOperationException(
                    $"Subscription '{subscription.Name}' is processed in this application but has no " +
                    $"handler for: {string.Join(", ", missingHandlers)}.");
            }
        }
    }

    private static string CreateProcessorId() =>
        $"{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid():N}";
}
