namespace IntegrationEventBus;

/// <summary>
/// Defines how a failed delivery is retried. The definition lives in application configuration;
/// the calculated attempt state and next execution time are persisted by the storage provider.
/// </summary>
public abstract record RetryPolicy
{
    /// <summary>Gets the default policy with immediate retries followed by deferred retries.</summary>
    public static LimitedImmediateRetries Default => new()
    {
        ImmediateRetryDelays =
        [
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(5),
            TimeSpan.FromSeconds(10)
        ],
        DeferredRetryDelay = TimeSpan.FromMinutes(15),
        MaxAttempts = 20,
        DeadLetterAfter = TimeSpan.FromHours(24)
    };

    /// <summary>Gets the stable name persisted with delivery state.</summary>
    public string Name { get; init; } = "default";

    /// <summary>Gets the policy version persisted with delivery state.</summary>
    public int Version { get; init; } = 1;

    internal abstract RetryDecision Plan(int failedAttempt, DateTimeOffset firstFailedAtUtc, DateTimeOffset nowUtc);

    internal virtual void Validate()
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            throw new InvalidOperationException("Retry policy name cannot be empty.");
        }

        if (Version < 1)
        {
            throw new InvalidOperationException($"Retry policy '{Name}' must have a positive version.");
        }
    }

    /// <summary>Retries indefinitely and blocks later deliveries in the subscription.</summary>
    public sealed record UnlimitedImmediateRetries : RetryPolicy
    {
        /// <summary>Gets the delays used for successive attempts; the last delay repeats indefinitely.</summary>
        public required TimeSpan[] ImmediateRetryDelays { get; init; }

        internal override void Validate()
        {
            base.Validate();
            ValidateDelays(ImmediateRetryDelays, requireAtLeastOne: true);
        }

        internal override RetryDecision Plan(int failedAttempt, DateTimeOffset firstFailedAtUtc, DateTimeOffset nowUtc)
        {
            var index = failedAttempt - 1;
            var delay = 
                index < ImmediateRetryDelays.Length
                ? ImmediateRetryDelays[index]
                : ImmediateRetryDelays[^1];

            return new RetryDecision(false, true, nowUtc + delay);
        }
    }

    /// <summary>Uses a finite immediate phase followed by non-blocking deferred retries.</summary>
    public sealed record LimitedImmediateRetries : RetryPolicy
    {
        /// <summary>Gets the delays for blocking immediate retries.</summary>
        public required TimeSpan[] ImmediateRetryDelays { get; init; }

        /// <summary>Gets the delay between non-blocking deferred retries.</summary>
        public required TimeSpan DeferredRetryDelay { get; init; }

        /// <summary>Gets the maximum number of failed attempts before dead-lettering.</summary>
        public required int MaxAttempts { get; init; }

        /// <summary>Gets the optional maximum time since the first failure before dead-lettering.</summary>
        public required TimeSpan? DeadLetterAfter { get; init; }

        internal override void Validate()
        {
            base.Validate();
            ValidateDelays(ImmediateRetryDelays, requireAtLeastOne: false);
            ValidateLimits(DeferredRetryDelay, MaxAttempts, DeadLetterAfter);
        }

        internal override RetryDecision Plan(int failedAttempt, DateTimeOffset firstFailedAtUtc, DateTimeOffset nowUtc)
        {
            if (IsDeadLetter(MaxAttempts, DeadLetterAfter, failedAttempt, firstFailedAtUtc, nowUtc))
            {
                return new RetryDecision(true, false, null);
            }

            var index = failedAttempt - 1;
            var isImmediateRetry = index < ImmediateRetryDelays.Length;
            var delay = isImmediateRetry ? ImmediateRetryDelays[index] : DeferredRetryDelay;
            return new RetryDecision(false, isImmediateRetry, nowUtc + delay);
        }
    }

    /// <summary>Uses non-blocking deferred retries without an immediate retry phase.</summary>
    public sealed record DeferredRetriesOnly : RetryPolicy
    {
        /// <summary>Gets the delay between deferred retries.</summary>
        public required TimeSpan DeferredRetryDelay { get; init; }

        /// <summary>Gets the maximum number of failed attempts before dead-lettering.</summary>
        public required int MaxAttempts { get; init; }

        /// <summary>Gets the optional maximum time since the first failure before dead-lettering.</summary>
        public required TimeSpan? DeadLetterAfter { get; init; }

        internal override void Validate()
        {
            base.Validate();
            ValidateLimits(DeferredRetryDelay, MaxAttempts, DeadLetterAfter);
        }

        internal override RetryDecision Plan(int failedAttempt, DateTimeOffset firstFailedAtUtc, DateTimeOffset nowUtc)
        {
            if (IsDeadLetter(MaxAttempts, DeadLetterAfter, failedAttempt, firstFailedAtUtc, nowUtc))
            {
                return new RetryDecision(true, false, null);
            }

            return new RetryDecision(false, false, nowUtc + DeferredRetryDelay);
        }
    }

    private static bool IsDeadLetter(
        int maxAttempts,
        TimeSpan? deadLetterAfter,
        int failedAttempt,
        DateTimeOffset firstFailedAtUtc,
        DateTimeOffset nowUtc)
    {
        return
            failedAttempt >= maxAttempts
            || (deadLetterAfter is { } lifetime && nowUtc - firstFailedAtUtc >= lifetime);
    }

    private static void ValidateDelays(TimeSpan[] delays, bool requireAtLeastOne)
    {
        if (delays is null)
        {
            throw new InvalidOperationException("Immediate retry delays must be defined.");
        }

        if (requireAtLeastOne && delays.Length == 0)
        {
            throw new InvalidOperationException(
                "An unlimited immediate retry policy must define at least one delay.");
        }

        if (delays.Any(static delay => delay < TimeSpan.Zero))
        {
            throw new InvalidOperationException("Retry delays cannot be negative.");
        }
    }

    private static void ValidateLimits(TimeSpan deferredRetryDelay, int maxAttempts, TimeSpan? deadLetterAfter)
    {
        if (deferredRetryDelay < TimeSpan.Zero)
        {
            throw new InvalidOperationException("Deferred retry delay cannot be negative.");
        }

        if (maxAttempts < 1)
        {
            throw new InvalidOperationException("A retry policy must allow at least one attempt.");
        }

        if (deadLetterAfter is { } lifetime && lifetime <= TimeSpan.Zero)
        {
            throw new InvalidOperationException("Dead-letter duration must be positive.");
        }
    }
}
