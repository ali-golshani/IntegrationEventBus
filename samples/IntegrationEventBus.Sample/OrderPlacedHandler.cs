using IntegrationEventBus;
using Microsoft.Extensions.Logging;

namespace IntegrationEventBus.Sample;

internal sealed class OrderPlacedHandler(ILogger<OrderPlacedHandler> logger)
    : IIntegrationEventHandler<OrderPlaced>
{
    public ValueTask HandleAsync(
        OrderPlaced integrationEvent,
        IntegrationEventContext context,
        CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Handling order {OrderId}; event {EventId}; attempt {Attempt}.",
            integrationEvent.OrderId,
            context.EventId,
            context.Attempt);

        Console.WriteLine(
            "Handling order {0}; event {1}; attempt {2}.",
            integrationEvent.OrderId,
            context.EventId,
            context.Attempt);

        return ValueTask.CompletedTask;
    }
}
