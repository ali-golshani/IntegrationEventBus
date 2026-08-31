using System.Data;
using System.Security.Cryptography;
using System.Text;
using IntegrationEventBus.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace IntegrationEventBus.SqlServer;

internal sealed class SqlServerSubscriptionRunner(
    SqlServerIntegrationEventBusOptions options,
    IIntegrationEventDispatcher dispatcher,
    IProcessorSignal processorSignal,
    ILogger<SqlServerSubscriptionRunner> logger)
    : ISubscriptionRunner
{
    private static readonly string AcquireSubscriptionLockQuery = Properties.Resources.AcquireSubscriptionLock;
    private static readonly string ClaimNextDeliveryQuery = Properties.Resources.ClaimNextDelivery;
    private static readonly string MarkFailedQuery = Properties.Resources.MarkFailed;
    private static readonly string MarkSucceededQuery = Properties.Resources.MarkSucceeded;
    private static readonly string ReleaseCancelledAttemptQuery = Properties.Resources.ReleaseCancelledAttempt;

    public async Task RunAsync(
        SubscriptionDefinition subscription,
        string processorId,
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await using var connection = new SqlConnection(options.ConnectionString);
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

                if (!await TryAcquireLockAsync(connection, subscription, cancellationToken).ConfigureAwait(false))
                {
                    logger.LogDebug(
                        "Processor {ProcessorId} could not acquire the lock for subscription {Subscription}.",
                        processorId,
                        subscription.Name);

                    await processorSignal.WaitAsync(options.LockRetryInterval, cancellationToken)
                        .ConfigureAwait(false);
                    continue;
                }

                logger.LogInformation(
                    "Processor {ProcessorId} acquired the lock for subscription {Subscription}.",
                    processorId,
                    subscription.Name);

                await ProcessUnderLockAsync(connection, subscription, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(
                    exception,
                    "Subscription {Subscription} processor failed and will reconnect.",
                    subscription.Name);
                await processorSignal.WaitAsync(options.LockRetryInterval, cancellationToken)
                    .ConfigureAwait(false);
            }
        }
    }

    private async Task ProcessUnderLockAsync(
        SqlConnection connection,
        SubscriptionDefinition subscription,
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var delivery = await TryClaimNextAsync(connection, subscription.Name, cancellationToken)
                .ConfigureAwait(false);

            if (delivery is null)
            {
                await processorSignal.WaitAsync(options.PollingInterval, cancellationToken)
                    .ConfigureAwait(false);
                continue;
            }

            try
            {
                await dispatcher.DispatchAsync(delivery, subscription, cancellationToken)
                    .ConfigureAwait(false);
                await MarkSucceededAsync(connection, delivery.DeliveryId, cancellationToken)
                    .ConfigureAwait(false);

                logger.LogDebug(
                    "Event {EventId} succeeded for subscription {Subscription} on attempt {Attempt}.",
                    delivery.EventId,
                    subscription.Name,
                    delivery.Attempt);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                await ReleaseCancelledAttemptAsync(connection, delivery.DeliveryId, cancellationToken: default)
                    .ConfigureAwait(false);
                throw;
            }
            catch (Exception exception)
            {
                var nowUtc = DateTimeOffset.UtcNow;
                var firstFailedAtUtc = delivery.FirstFailedAtUtc ?? nowUtc;
                var decision = RetryPlanner.Plan(
                    subscription.RetryPolicy,
                    delivery.Attempt,
                    firstFailedAtUtc,
                    nowUtc);

                await MarkFailedAsync(
                        connection,
                        delivery.DeliveryId,
                        firstFailedAtUtc,
                        nowUtc,
                        decision,
                        exception,
                        cancellationToken)
                    .ConfigureAwait(false);

                if (decision.IsDeadLetter)
                {
                    logger.LogError(
                        exception,
                        "Event {EventId} was dead-lettered for subscription {Subscription} after {Attempt} attempts.",
                        delivery.EventId,
                        subscription.Name,
                        delivery.Attempt);
                }
                else
                {
                    logger.LogWarning(
                        exception,
                        "Event {EventId} failed for subscription {Subscription} on attempt {Attempt}; " +
                        "next attempt is {NextAttemptAtUtc} and blocking is {BlocksFollowingEvents}.",
                        delivery.EventId,
                        subscription.Name,
                        delivery.Attempt,
                        decision.NextAttemptAtUtc,
                        decision.BlocksFollowingEvents);
                }
            }
        }
    }

    private async Task<bool> TryAcquireLockAsync(
        SqlConnection connection,
        SubscriptionDefinition subscription,
        CancellationToken cancellationToken)
    {
        var lockInput = $"{SqlServerConstants.SchemaName}:{subscription.Topic}:{subscription.Name}";
        var lockHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(lockInput)));
        var resource = $"IntegrationEventBus:{lockHash}";

        await using var command = connection.CreateCommand();
        command.CommandTimeout = options.CommandTimeoutSeconds;
        command.CommandText = AcquireSubscriptionLockQuery;
        command.Parameters.Add(new SqlParameter("@Resource", SqlDbType.NVarChar, 255) { Value = resource });

        var result = (int)(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("sp_getapplock did not return a result."));
        if (result == -1)
        {
            return false;
        }

        if (result < 0)
        {
            throw new InvalidOperationException(
                $"sp_getapplock failed for subscription '{subscription.Name}' with result {result}.");
        }

        return true;
    }

    private async Task<StoredEventDelivery?> TryClaimNextAsync(
        SqlConnection connection,
        string subscriptionName,
        CancellationToken cancellationToken)
    {
        var nowUtc = DateTimeOffset.UtcNow;

        await using var transaction = await connection.BeginTransactionAsync(
                IsolationLevel.ReadCommitted,
                cancellationToken)
            .ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.Transaction = (SqlTransaction)transaction;
        command.CommandTimeout = options.CommandTimeoutSeconds;
        command.CommandText = ClaimNextDeliveryQuery;

        command.Parameters.Add(new SqlParameter("@SubscriptionName", SqlDbType.NVarChar, 200) { Value = subscriptionName });
        command.Parameters.Add(new SqlParameter("@Pending", SqlDbType.TinyInt) { Value = (byte)DeliveryStatus.Pending });
        command.Parameters.Add(new SqlParameter("@Retrying", SqlDbType.TinyInt) { Value = (byte)DeliveryStatus.Retrying });
        command.Parameters.Add(new SqlParameter("@NowUtc", SqlDbType.DateTimeOffset) { Value = nowUtc });

        StoredEventDelivery? delivery = null;
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                delivery = new StoredEventDelivery(
                    DeliveryId: reader.GetInt64(0),
                    EventSequence: reader.GetInt64(1),
                    EventId: reader.GetGuid(2),
                    EventName: reader.GetString(3),
                    Topic: reader.GetString(4),
                    PayloadJson: reader.GetString(5),
                    OccurredAtUtc: reader.GetFieldValue<DateTimeOffset>(6),
                    CorrelationId: reader.IsDBNull(7) ? null : reader.GetString(7),
                    CausationId: reader.IsDBNull(8) ? null : reader.GetGuid(8),
                    Attempt: reader.GetInt32(9),
                    FirstFailedAtUtc: reader.IsDBNull(10)
                        ? null
                        : reader.GetFieldValue<DateTimeOffset>(10));
            }
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return delivery;
    }

    private Task MarkSucceededAsync(
        SqlConnection connection,
        long deliveryId,
        CancellationToken cancellationToken) =>
        ExecuteDeliveryUpdateAsync(
            connection,
            MarkSucceededQuery,
            deliveryId,
            command =>
            {
                command.Parameters.Add(new SqlParameter("@Status", SqlDbType.TinyInt)
                {
                    Value = (byte)DeliveryStatus.Succeeded
                });
                command.Parameters.Add(new SqlParameter("@NowUtc", SqlDbType.DateTimeOffset)
                {
                    Value = DateTimeOffset.UtcNow
                });
            },
            cancellationToken);

    private Task MarkFailedAsync(
        SqlConnection connection,
        long deliveryId,
        DateTimeOffset firstFailedAtUtc,
        DateTimeOffset nowUtc,
        RetryDecision decision,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var error = exception.ToString();
        if (error.Length > 32_768)
        {
            error = error[..32_768];
        }

        return ExecuteDeliveryUpdateAsync(
            connection,
            MarkFailedQuery,
            deliveryId,
            command =>
            {
                command.Parameters.Add(new SqlParameter("@Status", SqlDbType.TinyInt)
                {
                    Value = decision.IsDeadLetter
                        ? (byte)DeliveryStatus.DeadLettered
                        : (byte)DeliveryStatus.Retrying
                });
                command.Parameters.Add(new SqlParameter("@BlocksFollowing", SqlDbType.Bit)
                {
                    Value = decision.BlocksFollowingEvents
                });
                command.Parameters.Add(new SqlParameter("@NextAttemptAtUtc", SqlDbType.DateTimeOffset)
                {
                    Value = (object?)decision.NextAttemptAtUtc ?? DBNull.Value
                });
                command.Parameters.Add(new SqlParameter("@FirstFailedAtUtc", SqlDbType.DateTimeOffset)
                {
                    Value = firstFailedAtUtc
                });
                command.Parameters.Add(new SqlParameter("@LastError", SqlDbType.NVarChar, -1)
                {
                    Value = error
                });
                command.Parameters.Add(new SqlParameter("@CompletedAtUtc", SqlDbType.DateTimeOffset)
                {
                    Value = decision.IsDeadLetter ? nowUtc : DBNull.Value
                });
            },
            cancellationToken);
    }

    private Task ReleaseCancelledAttemptAsync(
        SqlConnection connection,
        long deliveryId,
        CancellationToken cancellationToken) =>
        ExecuteDeliveryUpdateAsync(
            connection,
            ReleaseCancelledAttemptQuery,
            deliveryId,
            command => command.Parameters.Add(
                new SqlParameter("@NowUtc", SqlDbType.DateTimeOffset) { Value = DateTimeOffset.UtcNow }),
            cancellationToken);

    private async Task ExecuteDeliveryUpdateAsync(
        SqlConnection connection,
        string commandText,
        long deliveryId,
        Action<SqlCommand> addParameters,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandTimeout = options.CommandTimeoutSeconds;
        command.CommandText = commandText;
        command.Parameters.Add(new SqlParameter("@DeliveryId", SqlDbType.BigInt) { Value = deliveryId });
        addParameters(command);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
