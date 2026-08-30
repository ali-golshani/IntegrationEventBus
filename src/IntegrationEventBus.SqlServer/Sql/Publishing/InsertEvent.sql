INSERT INTO {{Schema}}.[Events]
    ([Id], [EventName], [Topic], [PayloadJson], [HeadersJson], [OccurredAtUtc],
     [CreatedAtUtc], [CorrelationId], [CausationId])
VALUES
    (@Id, @EventName, @Topic, @PayloadJson, @HeadersJson, @OccurredAtUtc,
     SYSUTCDATETIME(), @CorrelationId, @CausationId);
