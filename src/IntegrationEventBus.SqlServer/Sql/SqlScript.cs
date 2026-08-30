namespace IntegrationEventBus.SqlServer;

internal enum SqlScript
{
    CreateSchema,
    InsertEvent,
    InsertDelivery,
    AcquireSubscriptionLock,
    ClaimNextDelivery,
    MarkSucceeded,
    MarkFailed,
    ReleaseCancelledAttempt
}
