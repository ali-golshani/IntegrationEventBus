using System.Data;
using System.Data.Common;
using IntegrationEventBus.Infrastructure;
using Microsoft.Data.SqlClient;

namespace IntegrationEventBus.SqlServer;

internal sealed class SqlServerIntegrationEventPublisher(
    IntegrationEventTopology topology,
    IIntegrationEventSerializer serializer,
    SqlServerIntegrationEventBusOptions options,
    ISqlScriptProvider scripts,
    IProcessorSignal processorSignal)
    : IIntegrationEventPublisher
{
    public async ValueTask<Guid> PublishAsync<TEvent>(
        TEvent integrationEvent,
        DbTransaction transaction,
        PublishOptions? publishOptions = null,
        CancellationToken cancellationToken = default)
        where TEvent : notnull
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        ArgumentNullException.ThrowIfNull(transaction);

        if (transaction is not SqlTransaction sqlTransaction)
        {
            throw new ArgumentException(
                "The SQL Server provider requires a Microsoft.Data.SqlClient.SqlTransaction.",
                nameof(transaction));
        }

        var connection = sqlTransaction.Connection
            ?? throw new InvalidOperationException("The supplied SQL transaction is no longer active.");

        if (connection.State != ConnectionState.Open)
        {
            throw new InvalidOperationException("The supplied SQL transaction connection is not open.");
        }

        var definition = topology.GetEvent(typeof(TEvent));
        var subscriptions = topology.GetSubscriptions(typeof(TEvent), definition.Topic);
        var eventId = publishOptions?.EventId ?? Guid.NewGuid();
        if (eventId == Guid.Empty)
        {
            throw new ArgumentException("EventId cannot be an empty GUID.", nameof(publishOptions));
        }

        var occurredAtUtc = (publishOptions?.OccurredAtUtc ?? DateTimeOffset.UtcNow).ToUniversalTime();
        var correlationId = publishOptions?.CorrelationId;
        if (correlationId?.Length > 200)
        {
            throw new ArgumentException("CorrelationId cannot be longer than 200 characters.", nameof(publishOptions));
        }

        var payloadJson = serializer.Serialize(integrationEvent, typeof(TEvent));
        await InsertEventAsync(
                connection,
                sqlTransaction,
                eventId,
                definition,
                payloadJson,
                occurredAtUtc,
                correlationId,
                publishOptions?.CausationId,
                cancellationToken)
            .ConfigureAwait(false);

        foreach (var subscription in subscriptions)
        {
            await InsertDeliveryAsync(
                    connection,
                    sqlTransaction,
                    eventId,
                    subscription,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        // This is a best-effort in-process optimization. Polling remains the source of truth,
        // because the transaction may not have committed yet and producers can run separately.
        processorSignal.Pulse();
        return eventId;
    }

    private async Task InsertEventAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        Guid eventId,
        IntegrationEventDefinition definition,
        string payloadJson,
        DateTimeOffset occurredAtUtc,
        string? correlationId,
        Guid? causationId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandTimeout = options.CommandTimeoutSeconds;
        command.CommandText = scripts.Get(SqlScript.InsertEvent);

        command.Parameters.Add(new SqlParameter("@Id", SqlDbType.UniqueIdentifier) { Value = eventId });
        command.Parameters.Add(new SqlParameter("@EventName", SqlDbType.NVarChar, 200) { Value = definition.EventName });
        command.Parameters.Add(new SqlParameter("@Topic", SqlDbType.NVarChar, 200) { Value = definition.Topic });
        command.Parameters.Add(new SqlParameter("@PayloadJson", SqlDbType.NVarChar, -1) { Value = payloadJson });
        command.Parameters.Add(new SqlParameter("@OccurredAtUtc", SqlDbType.DateTimeOffset) { Value = occurredAtUtc });
        command.Parameters.Add(new SqlParameter("@CorrelationId", SqlDbType.NVarChar, 200)
        {
            Value = (object?)correlationId ?? DBNull.Value
        });
        command.Parameters.Add(new SqlParameter("@CausationId", SqlDbType.UniqueIdentifier)
        {
            Value = (object?)causationId ?? DBNull.Value
        });

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task InsertDeliveryAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        Guid eventId,
        SubscriptionDefinition subscription,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandTimeout = options.CommandTimeoutSeconds;
        command.CommandText = scripts.Get(SqlScript.InsertDelivery);

        command.Parameters.Add(new SqlParameter("@EventId", SqlDbType.UniqueIdentifier) { Value = eventId });
        command.Parameters.Add(new SqlParameter("@SubscriptionName", SqlDbType.NVarChar, 200) { Value = subscription.Name });
        command.Parameters.Add(new SqlParameter("@Status", SqlDbType.TinyInt) { Value = (byte)DeliveryStatus.Pending });
        command.Parameters.Add(new SqlParameter("@RetryPolicyName", SqlDbType.NVarChar, 200)
        {
            Value = subscription.RetryPolicy.Name
        });
        command.Parameters.Add(new SqlParameter("@RetryPolicyVersion", SqlDbType.Int)
        {
            Value = subscription.RetryPolicy.Version
        });

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
