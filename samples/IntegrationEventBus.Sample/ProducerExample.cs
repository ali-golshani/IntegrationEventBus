using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace IntegrationEventBus.Sample;

internal static class ProducerExample
{
    const string connectionString =
        "Server=.;Database=IntegrationEventBusSample;User Id=golshani;Password=Ali_Golshani;TrustServerCertificate=True;";

    public static void AddIntegrationEventBus(this IServiceCollection services)
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
            subscription.UseRetryPolicy(
                RetryPolicyBuilder.UnlimitedImmediateRetries(
                [
                    TimeSpan.FromSeconds(1),
                    TimeSpan.FromSeconds(5),
                    TimeSpan.FromSeconds(30)
                ]) with
                {
                    Name = "external-api",
                    Version = 1
                });
        });
    }

    public static async Task<Guid> PublishAsync(
        IServiceProvider services,
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
