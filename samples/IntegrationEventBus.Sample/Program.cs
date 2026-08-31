using IntegrationEventBus;
using IntegrationEventBus.SqlServer;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

const string connectionString =
    "Server=.;Database=IntegrationEventBusSample;Integrated Security=True;TrustServerCertificate=True;";

var builder = Host.CreateApplicationBuilder(args);

builder.Services
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

var host = builder.Build();
await host.Services.GetRequiredService<SqlServerIntegrationEventBusMigrator>().MigrateAsync();
await host.RunAsync();

internal sealed record OrderPlaced(Guid OrderId, decimal Total);

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

        return ValueTask.CompletedTask;
    }
}

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
