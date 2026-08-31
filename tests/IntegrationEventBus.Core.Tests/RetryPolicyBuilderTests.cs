namespace IntegrationEventBus.Core.Tests;

public sealed class RetryPolicyBuilderTests
{
    [Fact]
    public void Default_creates_a_fresh_default_policy()
    {
        var first = RetryPolicyBuilder.Default();
        var second = RetryPolicyBuilder.Default();

        Assert.NotSame(first, second);
        Assert.Equal(RetryPolicy.Default.Name, first.Name);
        Assert.Equal(RetryPolicy.Default.Version, first.Version);
        Assert.Equal(RetryPolicy.Default.ImmediateRetryDelays, first.ImmediateRetryDelays);
        Assert.Equal(RetryPolicy.Default.MaxAttempts, first.MaxAttempts);
        Assert.Equal(RetryPolicy.Default.DeadLetterAfter, first.DeadLetterAfter);
    }

    [Fact]
    public void Unlimited_immediate_retries_repeat_the_last_delay_without_dead_letter_limits()
    {
        var policy = RetryPolicyBuilder.UnlimitedImmediateRetries(
            [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5)]);

        Assert.Equal(
            [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5)],
            policy.ImmediateRetryDelays);
        Assert.True(policy.RepeatLastImmediateRetryDelay);
        Assert.Equal(RetryPolicy.UnlimitedAttempts, policy.MaxAttempts);
        Assert.Null(policy.DeadLetterAfter);
    }

    [Fact]
    public void Limited_immediate_retries_accept_individual_delays()
    {
        var policy = RetryPolicyBuilder.LimitedImmediateRetries(
            [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(10)]);

        Assert.Equal(
            [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(10)],
            policy.ImmediateRetryDelays);
        Assert.False(policy.RepeatLastImmediateRetryDelay);
    }

    [Fact]
    public void Limited_immediate_retries_can_repeat_one_delay_a_fixed_number_of_times()
    {
        var policy = RetryPolicyBuilder.LimitedImmediateRetries(
            TimeSpan.FromSeconds(5),
            count: 3);

        Assert.Equal(
            [TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5)],
            policy.ImmediateRetryDelays);
    }

    [Fact]
    public void Deferred_only_policy_has_no_immediate_retries()
    {
        var policy = RetryPolicyBuilder.DeferredRetriesOnly();

        Assert.Empty(policy.ImmediateRetryDelays);
        Assert.False(policy.RepeatLastImmediateRetryDelay);
    }

    [Fact]
    public void Builder_copies_the_delay_array()
    {
        TimeSpan[] delays = [TimeSpan.FromSeconds(1)];
        var policy = RetryPolicyBuilder.LimitedImmediateRetries(delays);

        delays[0] = TimeSpan.FromMinutes(1);

        Assert.Equal(TimeSpan.FromSeconds(1), policy.ImmediateRetryDelays[0]);
    }
}
