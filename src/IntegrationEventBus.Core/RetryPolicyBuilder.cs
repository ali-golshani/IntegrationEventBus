namespace IntegrationEventBus;

/// <summary>
/// Creates retry policies for common retry strategies.
/// </summary>
public static class RetryPolicyBuilder
{
    /// <summary>
    /// Creates a policy with the default retry settings.
    /// </summary>
    public static RetryPolicy Default() => new();

    /// <summary>
    /// Creates a policy that repeats the final immediate delay and blocks following events for an
    /// effectively unlimited number of attempts.
    /// </summary>
    public static RetryPolicy UnlimitedImmediateRetries(TimeSpan[] delays) =>
        Build(
            new RetryPolicy
            {
                ImmediateRetryDelays = Copy(delays),
                RepeatLastImmediateRetryDelay = true,
                MaxAttempts = RetryPolicy.UnlimitedAttempts,
                DeadLetterAfter = null
            });

    /// <summary>
    /// Creates a policy with a finite sequence of immediate retries followed by deferred retries.
    /// </summary>
    public static RetryPolicy LimitedImmediateRetries(TimeSpan[] delays)
    {
        return Build(new RetryPolicy { ImmediateRetryDelays = Copy(delays) });
    }

    /// <summary>
    /// Creates a policy with a finite number of immediate retries that all use the same delay.
    /// </summary>
    public static RetryPolicy LimitedImmediateRetries(TimeSpan delay, int count)
    {
        return LimitedImmediateRetries(Enumerable.Repeat(delay, count).ToArray());
    }

    /// <summary>
    /// Creates a policy that immediately moves failed deliveries to non-blocking deferred retries.
    /// </summary>
    public static RetryPolicy DeferredRetriesOnly()
    {
        return Build(new RetryPolicy { ImmediateRetryDelays = [] });
    }

    private static RetryPolicy Build(RetryPolicy policy)
    {
        policy.Validate();
        return policy;
    }

    private static TimeSpan[] Copy(TimeSpan[] delays)
    {
        ArgumentNullException.ThrowIfNull(delays);
        return [.. delays];
    }
}
