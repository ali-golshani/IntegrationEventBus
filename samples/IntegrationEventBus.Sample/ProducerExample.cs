using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace IntegrationEventBus.Sample;

internal static class ProducerExample
{
    public static async Task<Guid> PublishAsync(
        IServiceProvider services,
        string connectionString,
        OrderPlaced integrationEvent,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        // Save business data here using the same connection and transaction.
        var publisher = services.GetRequiredService<IIntegrationEventPublisher>();
        var eventId = await publisher.PublishAsync(
            integrationEvent,
            transaction,
            new PublishOptions { CorrelationId = integrationEvent.OrderId.ToString("N") },
            cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return eventId;
    }
}
