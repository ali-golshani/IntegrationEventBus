# IntegrationEventBus

A local-first, SQL Server-backed integration event bus for .NET 10. It stores events in the same
transaction as business data and processes them asynchronously with explicit topics,
subscriptions, handlers, retries, and dead letters.

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

## Projects

| Project | Responsibility |
| --- | --- |
| `IntegrationEventBus.Abstractions` | Publisher, handler, context, and publish contracts |
| `IntegrationEventBus.Core` | Explicit topology, serialization, dispatch, and retry planning |
| `IntegrationEventBus.SqlServer` | Transactional publisher, schema, delivery state, polling, and locks |
| `IntegrationEventBus.Hosting` | Generic Host background processor |

SQL Server commands are stored as embedded `.sql` resources inside the provider package. Runtime
values are parameterized and the database objects use the fixed `cap` schema.

## Configuration

```csharp
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
```

No assembly scanning is performed. A producer-only process can define a delivery route without
referencing the handler assembly:

```csharp
topology.Subscription("billing", "orders", subscription =>
    subscription.Subscribe<OrderPlaced>());
```

Producer and processor applications must use the same stable event names, topics, subscription
names, and event-to-subscription routes.

## Transactional publishing

```csharp
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

## Database

Database migration is explicit and is never checked or executed during publishing or processing.
Run it once at an appropriate point during application deployment or startup:

```csharp
var host = builder.Build();
await host.Services
    .GetRequiredService<SqlServerIntegrationEventBusMigrator>()
    .MigrateAsync();
await host.RunAsync();
```

The migration creates the `cap` schema and its `Events` and `Deliveries` tables when missing. If
the migration has not been run, normal SQL operations fail with the corresponding SQL Server error.

See `samples/IntegrationEventBus.Sample` for a complete Generic Host example.
