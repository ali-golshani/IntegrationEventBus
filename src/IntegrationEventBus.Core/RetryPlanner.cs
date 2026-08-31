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

        return policy.Plan(failedAttempt, firstFailedAtUtc, nowUtc);
    }
}
