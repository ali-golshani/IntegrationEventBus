namespace IntegrationEventBus;

/// <summary>
/// Defines how a failed delivery is retried. The definition lives in application configuration;
/// the calculated attempt state and next execution time are persisted by the storage provider.
/// </summary>
public sealed record RetryPolicy
{
    public const int UnlimitedAttempts = int.MaxValue;

    public static RetryPolicy Default { get; } = new();

    public string Name { get; init; } = "default";
    public int Version { get; init; } = 1;

    /// <summary>
    /// Gets the delay for each immediate retry. Immediate retries block later events in the
    /// subscription.
    /// </summary>
    public TimeSpan[] ImmediateRetryDelays { get; init; } =
    [
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(10)
    ];

    /// <summary>
    /// Gets a value indicating whether the last immediate retry delay is repeated after all
    /// configured immediate retry delays have been used.
    /// </summary>
    public bool RepeatLastImmediateRetryDelay { get; init; }

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

        if (ImmediateRetryDelays is null)
        {
            throw new InvalidOperationException($"Retry policy '{Name}' must define immediate retry delays.");
        }

        if (ImmediateRetryDelays.Any(static delay => delay < TimeSpan.Zero)
            || DeferredRetryDelay < TimeSpan.Zero)
        {
            throw new InvalidOperationException($"Retry policy '{Name}' cannot have a negative delay.");
        }

        if (RepeatLastImmediateRetryDelay && ImmediateRetryDelays.Length == 0)
        {
            throw new InvalidOperationException(
                $"Retry policy '{Name}' must define an immediate retry delay before it can be repeated.");
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
