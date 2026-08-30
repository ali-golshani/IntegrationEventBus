namespace IntegrationEventBus;

/// <summary>
/// The result of evaluating a failed delivery against its current retry policy.
/// </summary>
public sealed record RetryDecision(
    bool IsDeadLetter,
    bool BlocksFollowingEvents,
    DateTimeOffset? NextAttemptAtUtc);

/// <summary>
/// Calculates retry state without performing I/O.
/// </summary>
public static class RetryPlanner
{
    public static RetryDecision Plan(
        RetryPolicy policy,
        int failedAttempt,
        DateTimeOffset firstFailedAtUtc,
        DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(policy);
        policy.Validate();

        if (failedAttempt < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(failedAttempt));
        }

        if (firstFailedAtUtc > nowUtc)
        {
            throw new ArgumentOutOfRangeException(nameof(firstFailedAtUtc));
        }

        var lifetimeExpired = policy.DeadLetterAfter is { } lifetime
            && nowUtc - firstFailedAtUtc >= lifetime;

        if (failedAttempt >= policy.MaxAttempts || lifetimeExpired)
        {
            return new RetryDecision(true, false, null);
        }

        var isImmediateRetry = failedAttempt <= policy.ImmediateRetryCount;
        var delay = isImmediateRetry
            ? policy.ImmediateRetryDelay
            : policy.DeferredRetryDelay;

        return new RetryDecision(
            IsDeadLetter: false,
            BlocksFollowingEvents: isImmediateRetry,
            NextAttemptAtUtc: nowUtc + delay);
    }
}
