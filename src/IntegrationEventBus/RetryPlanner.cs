namespace IntegrationEventBus;

/// <summary>
/// Calculates retry state without performing I/O.
/// </summary>
public static class RetryPlanner
{
    /// <summary>Calculates the outcome of a failed delivery attempt.</summary>
    /// <param name="policy">The retry policy to apply.</param>
    /// <param name="failedAttempt">The one-based number of the failed attempt.</param>
    /// <param name="firstFailedAtUtc">The time at which the delivery first failed.</param>
    /// <param name="nowUtc">The time of the current failure.</param>
    /// <returns>The next persisted retry state.</returns>
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
