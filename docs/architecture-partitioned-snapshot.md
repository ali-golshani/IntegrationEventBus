# Partitioned processing architecture snapshot

> Status: superseded design snapshot  
> Captured: 2026-08-30  
> Purpose: preserve the partition-aware design so it can be revisited later. The active design intentionally simplifies each topic to a single processing lane.

## Goal

The library is a local SQL Server-backed integration-event system for .NET. Business data, an event envelope, and its subscription deliveries are inserted atomically in the caller's SQL transaction. Background processors communicate only through SQL Server and provide at-least-once delivery.

## Stable terminology

| Term | Meaning |
|---|---|
| Integration event | Immutable business message payload. |
| Event name | Stable contract identifier independent from the CLR type name. |
| Topic | Logical ordered stream containing one or more event types. |
| Partition key | Business key used to choose a partition. |
| Partition ID | Resolved ordering and concurrency partition inside a topic. |
| Subscription | Durable logical consumer of selected event types from one topic. |
| Handler | Strongly typed code bound to one event type inside a subscription. |
| Delivery | Durable delivery of one event to one subscription. |
| Processing lane | `(SubscriptionId, TopicName, PartitionId)`; the unit of ordering, scheduling, and distributed ownership. |
| Processor | Hosted background runtime that discovers and executes ready lanes. |
| Processor instance | One physical process running the processor. |

Each subscription belongs to one topic and explicitly selects event types from that topic. For each selected event type, a subscription has exactly one handler. If the same event needs two independent handlers, they are modeled as two subscriptions. If one handler must run after another, the first handler emits a new event after succeeding.

## Logical modules

```text
CAP.Abstractions
    Public event, handler, outbox, context, and topology contracts

CAP.Core
    Topology registry, routing, serialization orchestration,
    dispatch, retry decisions, and processing state machine

CAP.SqlServer
    Event/delivery persistence, lane state, claims, leases,
    SQL queries, schema management, and wake source

CAP.Hosting
    Background service, scheduler, bounded channel,
    fixed worker pool, and graceful shutdown
```

Dependency direction:

```text
CAP.Abstractions <- CAP.Core <- CAP.SqlServer
                         ^
                         |
                    CAP.Hosting
```

## Public surface

Conceptual application-facing contracts:

```csharp
public interface IIntegrationEventOutbox
{
    ValueTask<Guid> EnqueueAsync<TEvent>(
        TEvent message,
        DbTransaction transaction,
        PublishContext? context = null,
        CancellationToken cancellationToken = default);
}

public interface IIntegrationEventHandler<in TEvent>
{
    Task HandleAsync(
        TEvent message,
        IntegrationEventContext context,
        CancellationToken cancellationToken);
}
```

Topology and handler implementation registration are separate. Producers share the topology so they know which subscription deliveries to materialize, but do not reference handler implementation types. Processor applications bind their active subscriptions to handlers explicitly; no assembly scanning is required.

## Persistence model

### Events

Stores the immutable envelope once:

```text
EventId
SequenceNumber
EventName
SchemaVersion
Payload
ContentType
OccurredAtUtc
StoredAtUtc
CorrelationId
CausationId
TraceParent
```

### EventDeliveries

Stores one row per `(EventId, SubscriptionId)`:

```text
EventId
SubscriptionId
TopicName
PartitionKey
PartitionId
SequenceNumber
Status
AttemptCount
RetryStage
FirstFailedAtUtc
NextAttemptAtUtc
LockToken
LockedUntilUtc
LastError
CompletedAtUtc
RowVersion
```

Delivery states:

```text
Ready
Processing
WaitingOrdered
WaitingDeferred
Succeeded
DeadLettered
```

### SubscriptionPartitions

Stores scheduling state only for partitions that have existed:

```text
SubscriptionId
TopicName
PartitionId
State
NextExecutionAtUtc
LeaseToken
LeaseUntilUtc
WorkVersion
LastActivityAtUtc
RowVersion
```

Partition states:

```text
Ready
Running
Waiting
Idle
```

The publisher inserts business data, the event, all matching deliveries, and creates or updates the affected subscription-partition rows in the same SQL transaction.

## Scheduling model

There is no permanent task or loop per partition. Hosting uses:

```text
one scheduler loop
    -> atomic claim of ready lanes in SQL Server
    -> bounded Channel<LaneClaim>
    -> fixed worker pool
```

The scheduler reserves local capacity before claiming SQL rows. A claim changes a ready or due lane to `Running` and assigns a lease token. Only claimed lanes enter the bounded channel. A queued or executing lane occupies one processing slot.

Each worker processes one lane sequentially for a bounded turn:

```text
MaxDeliveriesPerTurn
or MaxProcessingTimePerTurn
or first ordered failure
or empty lane
```

This quantum prevents a hot partition from starving other partitions. A lane with remaining work becomes `Ready` again and returns through the scheduler.

Parallelism exists only across lanes:

```text
concurrency within a lane = 1
concurrency across lanes = MaxConcurrentPartitions
```

Idle and retry-waiting partitions consume no thread, task, or worker slot. The database stores their state and next execution time.

## Retry model

Retries have three phases:

1. **Immediate retry**: short delays while the same worker still owns the lane.
2. **Ordered redelivery**: durable retry time; the failed delivery blocks later deliveries in its lane, but the worker is released.
3. **Deferred redelivery**: after an attempt or elapsed-time threshold, the failed delivery no longer blocks later deliveries in its lane.

Transition from ordered to deferred can be bounded by both attempt count and elapsed time since `FirstFailedAtUtc`. Exhausted retries move the delivery to `DeadLettered`.

## Polling and wake-up

SQL Server is always the source of truth. A wake signal only interrupts the scheduler's wait and causes another database query. Lost signals cannot lose events because adaptive polling remains the fallback.

The scheduler waits until the earlier of:

```text
nearest NextExecutionAtUtc
maximum idle polling interval
```

## Delivery guarantees

The model is at-least-once. A process can fail after handler side effects but before completion is recorded, so handlers must be idempotent. Consumer-side transactional integration remains an open design question if handler business changes, emitted events, and delivery completion must share one SQL transaction.

## Main complexity introduced by partitions

Dynamic partitions require all of the following:

- a durable partition/lane scheduling table;
- a scheduler separate from message execution;
- bounded dispatch and worker capacity accounting;
- lane claims, leases, and recovery;
- fairness/processing quantum;
- concurrent lane-state transitions during publishing and processing;
- a `WorkVersion` or equivalent guard against incorrectly marking a lane idle while new work is committed.

The active simplified design removes partitions because current business requirements do not need this concurrency model.
