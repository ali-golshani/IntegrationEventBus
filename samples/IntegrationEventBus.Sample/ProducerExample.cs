using IntegrationEventBus;
using IntegrationEventBus.Topology;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace IntegrationEventBus.Sample;

internal static class ProducerExample
{
    public static void AddIntegrationEventBus(
        this IServiceCollection services,
        string connectionString)
    {
        services
            .AddIntegrationEventBus(topology =>
            {
                topology.AddSampleEvents();
            })
            .UseSqlServer(connectionString)
            .AddHostedProcessor();
    }

    private static void AddSampleEvents(this IntegrationEventTopologyBuilder topology)
    {
        topology.Event<OrderPlaced>("orders.placed", "orders");
        topology.Subscription("billing", "orders", subscription =>
        {
            subscription.Handle<OrderPlaced, OrderPlacedHandler>();
            subscription.UseRetryPolicy(new RetryPolicy.UnlimitedImmediateRetries
            {
                Name = "external-api",
                Version = 1,
                ImmediateRetryDelays =
                [
                    TimeSpan.FromSeconds(1),
                    TimeSpan.FromSeconds(5),
                    TimeSpan.FromSeconds(30)
                ]
            });
        });
    }

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

internal sealed record SampleDatabaseOptions(string ConnectionString);
