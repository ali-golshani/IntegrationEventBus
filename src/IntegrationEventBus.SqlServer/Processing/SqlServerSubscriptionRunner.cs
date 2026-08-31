using IntegrationEventBus.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using System.Data;
using System.Security.Cryptography;
using System.Text;

namespace IntegrationEventBus.SqlServer;

internal sealed class SqlServerSubscriptionRunner(
    SqlServerIntegrationEventBusOptions options,
    IIntegrationEventDispatcher dispatcher,
    IProcessorSignal processorSignal,
    ILogger<SqlServerSubscriptionRunner> logger)
    : ISubscriptionRunner
{
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
        var result = await SqlServerQueries.AcquireSubscriptionLockAsync(
            command,
            resource,
            cancellationToken);

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
        var delivery = await SqlServerQueries.ClaimNextDeliveryAsync(
            command,
            subscriptionName,
            (byte)DeliveryStatus.Pending,
            (byte)DeliveryStatus.Retrying,
            nowUtc,
            cancellationToken);

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return delivery;
    }

    private async Task MarkSucceededAsync(
        SqlConnection connection,
        long deliveryId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandTimeout = options.CommandTimeoutSeconds;
        await SqlServerQueries.MarkSucceededAsync(
            command,
            deliveryId,
            (byte)DeliveryStatus.Succeeded,
            DateTimeOffset.UtcNow,
            cancellationToken);
    }

    private async Task MarkFailedAsync(
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

        await using var command = connection.CreateCommand();
        command.CommandTimeout = options.CommandTimeoutSeconds;
        await SqlServerQueries.MarkFailedAsync(
            command,
            deliveryId,
            decision.IsDeadLetter
                ? (byte)DeliveryStatus.DeadLettered
                : (byte)DeliveryStatus.Retrying,
            decision.BlocksFollowingEvents,
            decision.NextAttemptAtUtc,
            firstFailedAtUtc,
            error,
            decision.IsDeadLetter ? nowUtc : null,
            cancellationToken);
    }

    private async Task ReleaseCancelledAttemptAsync(
        SqlConnection connection,
        long deliveryId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandTimeout = options.CommandTimeoutSeconds;
        await SqlServerQueries.ReleaseCancelledAttemptAsync(
            command,
            deliveryId,
            DateTimeOffset.UtcNow,
            cancellationToken);
    }
}
