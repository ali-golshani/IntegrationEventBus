namespace IntegrationEventBus.Core.Tests;

public sealed class RetryPlannerTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 30, 12, 0, 0, TimeSpan.Zero);

    private static readonly RetryPolicy Policy = new()
    {
        ImmediateRetryDelays =
        [
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(5),
            TimeSpan.FromSeconds(30)
        ],
        DeferredRetryDelay = TimeSpan.FromMinutes(15),
        MaxAttempts = 10,
        DeadLetterAfter = TimeSpan.FromHours(2)
    };

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 5)]
    [InlineData(3, 30)]
    public void Immediate_failures_use_their_configured_delay_and_block_following_events(
        int attempt,
        int expectedDelaySeconds)
    {
        var decision = RetryPlanner.Plan(Policy, attempt, Now, Now);

        Assert.False(decision.IsDeadLetter);
        Assert.True(decision.BlocksFollowingEvents);
        Assert.Equal(Now + TimeSpan.FromSeconds(expectedDelaySeconds), decision.NextAttemptAtUtc);
    }

    [Fact]
    public void Later_failures_are_deferred_and_do_not_block()
    {
        var decision = RetryPlanner.Plan(Policy, 4, Now, Now);

        Assert.False(decision.IsDeadLetter);
        Assert.False(decision.BlocksFollowingEvents);
        Assert.Equal(Now + TimeSpan.FromMinutes(15), decision.NextAttemptAtUtc);
    }

    [Fact]
    public void Empty_immediate_delays_defer_the_first_retry()
    {
        var policy = Policy with { ImmediateRetryDelays = [] };

        var decision = RetryPlanner.Plan(policy, 1, Now, Now);

        Assert.False(decision.IsDeadLetter);
        Assert.False(decision.BlocksFollowingEvents);
        Assert.Equal(Now + TimeSpan.FromMinutes(15), decision.NextAttemptAtUtc);
    }

    [Fact]
    public void Last_immediate_delay_can_repeat_and_block_following_events()
    {
        var policy = Policy with
        {
            RepeatLastImmediateRetryDelay = true,
            MaxAttempts = RetryPolicy.UnlimitedAttempts,
            DeadLetterAfter = null
        };

        var decision = RetryPlanner.Plan(policy, 100, Now, Now);

        Assert.False(decision.IsDeadLetter);
        Assert.True(decision.BlocksFollowingEvents);
        Assert.Equal(Now + TimeSpan.FromSeconds(30), decision.NextAttemptAtUtc);
    }

    [Fact]
    public void Repeating_the_last_immediate_delay_requires_a_delay()
    {
        var policy = Policy with
        {
            ImmediateRetryDelays = [],
            RepeatLastImmediateRetryDelay = true
        };

        Assert.Throws<InvalidOperationException>(() => RetryPlanner.Plan(policy, 1, Now, Now));
    }

    [Fact]
    public void Immediate_delays_cannot_be_negative()
    {
        var policy = Policy with { ImmediateRetryDelays = [TimeSpan.FromSeconds(-1)] };

        Assert.Throws<InvalidOperationException>(() => RetryPlanner.Plan(policy, 1, Now, Now));
    }

    [Fact]
    public void Maximum_attempt_count_dead_letters_the_delivery()
    {
        var decision = RetryPlanner.Plan(Policy, 10, Now, Now);

        Assert.True(decision.IsDeadLetter);
        Assert.Null(decision.NextAttemptAtUtc);
    }

    [Fact]
    public void Failure_lifetime_can_dead_letter_before_maximum_attempts()
    {
        var decision = RetryPlanner.Plan(Policy, 4, Now - TimeSpan.FromHours(2), Now);

        Assert.True(decision.IsDeadLetter);
        Assert.Null(decision.NextAttemptAtUtc);
    }
}
