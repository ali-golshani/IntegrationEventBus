namespace IntegrationEventBus.Tests;

public sealed class RetryPolicyTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 30, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Default_policy_uses_its_immediate_retry_schedule()
    {
        var decision = RetryPlanner.Plan(RetryPolicy.Default, 2, Now, Now);

        Assert.False(decision.IsDeadLetter);
        Assert.True(decision.BlocksFollowingEvents);
        Assert.Equal(Now + TimeSpan.FromSeconds(5), decision.NextAttemptAtUtc);
    }

    [Fact]
    public void Default_returns_a_fresh_limited_policy()
    {
        var first = RetryPolicy.Default;
        var second = RetryPolicy.Default;

        Assert.NotSame(first, second);
        Assert.NotSame(first.ImmediateRetryDelays, second.ImmediateRetryDelays);
    }

    [Fact]
    public void Deferred_only_policy_never_blocks_following_events()
    {
        var decision = RetryPlanner.Plan(
            new RetryPolicy.DeferredRetriesOnly
            {
                DeferredRetryDelay = TimeSpan.FromMinutes(15),
                MaxAttempts = 20,
                DeadLetterAfter = TimeSpan.FromHours(24)
            },
            1,
            Now,
            Now);

        Assert.False(decision.IsDeadLetter);
        Assert.False(decision.BlocksFollowingEvents);
        Assert.Equal(Now + TimeSpan.FromMinutes(15), decision.NextAttemptAtUtc);
    }

    [Fact]
    public void Retry_policy_uses_the_configured_delay_array()
    {
        TimeSpan[] delays = [TimeSpan.FromSeconds(1)];
        var policy = new RetryPolicy.UnlimitedImmediateRetries
        {
            ImmediateRetryDelays = delays
        };

        delays[0] = TimeSpan.FromMinutes(1);

        Assert.Equal(TimeSpan.FromMinutes(1), policy.ImmediateRetryDelays[0]);
    }
}
