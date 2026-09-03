# IntegrationEventBus

A local-first, SQL Server-backed integration event bus for .NET 10. It stores events in the same
transaction as business data and processes them asynchronously with explicit topics,
subscriptions, handlers, retries, and dead letters.

## Requirements

- .NET 10
- SQL Server
- Permission to create the `cap` schema and its tables when running the migration

## Installation

```shell
dotnet add package Minimal.IntegrationEventBus
```

## Guarantees

- Publishing the event and its delivery rows uses the caller's `SqlTransaction`.
- Handlers run outside a database transaction with at-least-once delivery semantics.
- A SQL Server application lock allows only one active processor for each subscription.
- Events are processed serially inside a subscription and concurrently across subscriptions.
- Immediate retries block later events. Deferred retries persist their next execution time and let
  later events proceed.
- Handler completion means success. An exception means failure.

Handlers that call external systems should use `IntegrationEventContext.EventId` as an idempotency
key whenever the external system supports one.

## Getting started

Define an event and its handler:

```csharp
using IntegrationEventBus;

public sealed record OrderPlaced(Guid OrderId, decimal Total);

public sealed class OrderPlacedHandler : IIntegrationEventHandler<OrderPlaced>
{
    public ValueTask HandleAsync(
        OrderPlaced integrationEvent,
        IntegrationEventContext context,
        CancellationToken cancellationToken)
    {
        // Handle the event. Throwing an exception marks this attempt as failed.
        return ValueTask.CompletedTask;
    }
}
```

Register the topology, SQL Server storage, and hosted processor:

```csharp
using IntegrationEventBus;
using Microsoft.Extensions.DependencyInjection;

services
    .AddIntegrationEventBus(topology =>
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
    })
    .UseSqlServer(connectionString)
    .AddHostedProcessor();
```

The connection string should come from your application's configuration or secret store. Do not
commit credentials to source control.

No assembly scanning is performed. A producer-only process can define a delivery route without
referencing the handler assembly:

```csharp
topology.Subscription("billing", "orders", subscription =>
    subscription.Subscribe<OrderPlaced>());
```

Producer and processor applications must use the same stable event names, topics, subscription
names, and event-to-subscription routes.

## Event contract compatibility

Event payloads may remain in SQL Server while applications are upgraded. Changes to an event type
must therefore remain compatible with payloads written by older application versions. Prefer
adding optional properties with suitable defaults, and avoid renaming, removing, or changing the
type of an existing property while older events may still be pending.

When a breaking contract change is unavoidable, register a new CLR event type with a new stable
event name and keep the previous contract available until its pending deliveries have completed.

## Transactional publishing

```csharp
using IntegrationEventBus;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

var publisher = services.GetRequiredService<IIntegrationEventPublisher>();

await using var connection = new SqlConnection(connectionString);
await connection.OpenAsync(cancellationToken);
await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

// Save business data with this connection and transaction.

var eventId = await publisher.PublishAsync(
    new OrderPlaced(orderId, total),
    transaction,
    new PublishOptions { CorrelationId = orderId.ToString("N") },
    cancellationToken);

await transaction.CommitAsync(cancellationToken);
```

Returning an event ID does not mean the transaction has committed.

## Retry persistence

The retry definition remains in application configuration. SQL Server persists the runtime state:

- attempt count;
- blocking or deferred state;
- first and last failure times;
- next attempt time;
- last error;
- retry policy name and version;
- succeeded or dead-lettered status.

Changing a policy affects the next failure calculation. An already persisted `NextAttemptAtUtc`
is not recalculated during application startup.

Use `RetryPolicy.UnlimitedImmediateRetries` to keep retrying the blocking event with the final
immediate delay. Later events in the subscription remain blocked until it succeeds.

## Database

Database migration is explicit and is never checked or executed during publishing or processing.
Run it once at an appropriate point during application deployment or startup:

```csharp
var host = builder.Build();
await host.Services
    .GetRequiredService<EventBusMigrator>()
    .MigrateAsync();
await host.RunAsync();
```

The migration creates the `cap` schema and its `Events` and `Deliveries` tables when missing. If
the migration has not been run, normal SQL operations fail with the corresponding SQL Server error.

SQL Server commands are embedded in the package, runtime values are parameterized, and database
objects use the fixed `cap` schema.

See the
[Generic Host sample](https://github.com/ali-golshani/IntegrationEventBus/tree/main/samples/IntegrationEventBus.Sample)
for a complete application example.
