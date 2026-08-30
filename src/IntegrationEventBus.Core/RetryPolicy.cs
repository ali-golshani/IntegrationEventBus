namespace IntegrationEventBus;

/// <summary>
/// Defines how a failed delivery is retried. The definition lives in application configuration;
/// the calculated attempt state and next execution time are persisted by the storage provider.
/// </summary>
public sealed record RetryPolicy
{
    public static RetryPolicy Default { get; } = new();

    public string Name { get; init; } = "default";

    public int Version { get; init; } = 1;

    /// <summary>
    /// Gets the number of short-delay retries that block later events in the subscription.
    /// </summary>
    public int ImmediateRetryCount { get; init; } = 3;

    public TimeSpan ImmediateRetryDelay { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Gets the delay used after immediate retries are exhausted. Deferred retries do not block
    /// later events in the subscription.
    /// </summary>
    public TimeSpan DeferredRetryDelay { get; init; } = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Gets the maximum number of handler invocations, including the initial invocation.
    /// </summary>
    public int MaxAttempts { get; init; } = 20;

    /// <summary>
    /// Gets the maximum failure lifetime. Set to <see langword="null"/> to use only
    /// <see cref="MaxAttempts"/>.
    /// </summary>
    public TimeSpan? DeadLetterAfter { get; init; } = TimeSpan.FromHours(24);

    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            throw new InvalidOperationException("Retry policy name cannot be empty.");
        }

        if (Version < 1)
        {
            throw new InvalidOperationException($"Retry policy '{Name}' must have a positive version.");
        }

        if (ImmediateRetryCount < 0)
        {
            throw new InvalidOperationException($"Retry policy '{Name}' cannot have a negative immediate retry count.");
        }

        if (ImmediateRetryDelay < TimeSpan.Zero || DeferredRetryDelay < TimeSpan.Zero)
        {
            throw new InvalidOperationException($"Retry policy '{Name}' cannot have a negative delay.");
        }

        if (MaxAttempts < 1)
        {
            throw new InvalidOperationException($"Retry policy '{Name}' must allow at least one attempt.");
        }

        if (DeadLetterAfter is { } deadLetterAfter && deadLetterAfter <= TimeSpan.Zero)
        {
            throw new InvalidOperationException($"Retry policy '{Name}' must have a positive dead-letter duration.");
        }
    }
}
