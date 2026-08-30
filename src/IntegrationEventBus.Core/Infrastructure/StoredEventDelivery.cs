namespace IntegrationEventBus.Infrastructure;

/// <summary>
/// Storage-neutral representation of an event delivery selected for execution.
/// </summary>
public sealed record StoredEventDelivery(
    long DeliveryId,
    long EventSequence,
    Guid EventId,
    string EventName,
    string Topic,
    string PayloadJson,
    string HeadersJson,
    DateTimeOffset OccurredAtUtc,
    string? CorrelationId,
    Guid? CausationId,
    int Attempt,
    DateTimeOffset? FirstFailedAtUtc);
