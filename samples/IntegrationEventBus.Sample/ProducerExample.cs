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
                topology.Event<OrderPlaced>("orders.placed", "orders");
                topology.Subscription("billing", "orders", subscription =>
                {
                    subscription.Handle<OrderPlaced, OrderPlacedHandler>();
                    subscription.UseRetryPolicy(new RetryPolicy
                    {
                        Name = "external-api",
                        Version = 1,
                        ImmediateRetryCount = 3,
                        ImmediateRetryDelay = TimeSpan.FromSeconds(5),
                        DeferredRetryDelay = TimeSpan.FromMinutes(15),
                        MaxAttempts = 20,
                        DeadLetterAfter = TimeSpan.FromHours(6)
                    });
                });
            })
            .UseSqlServer(connectionString)
            .AddHostedProcessor();
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
