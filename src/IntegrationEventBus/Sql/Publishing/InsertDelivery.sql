INSERT INTO [eventbus].[Deliveries]
    ([EventId], [SubscriptionName], [Status], [AttemptCount], [BlocksFollowing],
     [NextAttemptAtUtc], [RetryPolicyName], [RetryPolicyVersion])
VALUES
    (@EventId, @SubscriptionName, @Status, 0, 1, SYSUTCDATETIME(),
     @RetryPolicyName, @RetryPolicyVersion);
