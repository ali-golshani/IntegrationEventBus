namespace IntegrationEventBus.Core.Tests;

public sealed class RetryPlannerTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 30, 12, 0, 0, TimeSpan.Zero);

    private static readonly RetryPolicy Policy = new()
    {
        ImmediateRetryCount = 3,
        ImmediateRetryDelay = TimeSpan.FromSeconds(5),
        DeferredRetryDelay = TimeSpan.FromMinutes(15),
        MaxAttempts = 10,
        DeadLetterAfter = TimeSpan.FromHours(2)
    };

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Immediate_failures_block_following_events(int attempt)
    {
        var decision = RetryPlanner.Plan(Policy, attempt, Now, Now);

        Assert.False(decision.IsDeadLetter);
        Assert.True(decision.BlocksFollowingEvents);
        Assert.Equal(Now + TimeSpan.FromSeconds(5), decision.NextAttemptAtUtc);
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
