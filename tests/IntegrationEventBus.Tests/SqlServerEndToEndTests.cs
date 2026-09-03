using System.Data;
using IntegrationEventBus;
using IntegrationEventBus.Internal;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace IntegrationEventBus.Tests;

public sealed class SqlServerEndToEndTests
{
    [Fact]
    public async Task Transactional_event_is_persisted_and_processed()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var databaseName = $"IntegrationEventBusTest_{Guid.NewGuid():N}";
        var masterConnectionString =
            "Server=.;Database=master;Integrated Security=True;TrustServerCertificate=True;";
        var databaseConnectionString = new SqlConnectionStringBuilder(masterConnectionString)
        {
            InitialCatalog = databaseName
        }.ConnectionString;

        await CreateDatabaseAsync(masterConnectionString, databaseName);
        try
        {
            var probe = new HandlerProbe();
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton(probe);
            var bus = services.AddIntegrationEventBus(topology =>
            {
                topology.Event<TestEvent>("tests.created", "tests");
                topology.Subscription("test-handler", "tests", subscription =>
                    subscription.Handle<TestEvent, TestEventHandler>());
            });
            bus.UseSqlServer(databaseConnectionString, options =>
            {
                options.PollingInterval = TimeSpan.FromMilliseconds(50);
                options.LockRetryInterval = TimeSpan.FromMilliseconds(50);
            });

            await using var provider = services.BuildServiceProvider();
            var publisher = provider.GetRequiredService<IIntegrationEventPublisher>();

            await using (var connection = new SqlConnection(databaseConnectionString))
            {
                await connection.OpenAsync();
                await using var transaction = await connection.BeginTransactionAsync();
                await publisher.PublishAsync(
                    new TestEvent(42),
                    transaction,
                    cancellationToken: CancellationToken.None);
                await transaction.CommitAsync(CancellationToken.None);
            }

            await using (var connection = new SqlConnection(databaseConnectionString))
            {
                await connection.OpenAsync();
                await using var transaction = await connection.BeginTransactionAsync();
                await publisher.PublishAsync(
                    new TestEvent(99),
                    transaction,
                    cancellationToken: CancellationToken.None);
                await transaction.RollbackAsync(CancellationToken.None);
            }

            var (eventCount, deliveryCount) = await ReadRecordCountsAsync(databaseConnectionString);
            Assert.Equal(1, eventCount);
            Assert.Equal(1, deliveryCount);

            using var processorCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var runner = provider.GetRequiredService<ISubscriptionRunner>();
            var subscription = bus.Topology.GetSubscription("test-handler");
            var processor = runner.RunAsync(subscription, "integration-test", processorCancellation.Token);

            var handled = await probe.Handled.Task.WaitAsync(
                TimeSpan.FromSeconds(5),
                CancellationToken.None);
            Assert.Equal(42, handled.Value);

            await WaitForSucceededDeliveryAsync(
                databaseConnectionString,
                CancellationToken.None);

            await processorCancellation.CancelAsync();
            await processor;
        }
        finally
        {
            SqlConnection.ClearAllPools();
            await DropDatabaseAsync(masterConnectionString, databaseName);
        }
    }

    private static async Task CreateDatabaseAsync(string connectionString, string databaseName)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"CREATE DATABASE [{databaseName}]";
        await command.ExecuteNonQueryAsync();
    }

    private static async Task DropDatabaseAsync(string connectionString, string databaseName)
    {
        if (!databaseName.StartsWith("IntegrationEventBusTest_", StringComparison.Ordinal)
            || databaseName.Length != "IntegrationEventBusTest_".Length + 32)
        {
            throw new InvalidOperationException("Refusing to drop a database with an unexpected name.");
        }

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $$"""
            IF DB_ID(N'{{databaseName}}') IS NOT NULL
            BEGIN
                ALTER DATABASE [{{databaseName}}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                DROP DATABASE [{{databaseName}}];
            END;
            """;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task WaitForSucceededDeliveryAsync(
        string connectionString,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(5);
        while (DateTimeOffset.UtcNow < deadline)
        {
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT TOP (1) [Status]
                FROM [IntegrationEventBus].[Deliveries]
                WHERE [SubscriptionName] = N'test-handler';
                """;
            var status = await command.ExecuteScalarAsync(cancellationToken);
            if (status is byte value && value == 2)
            {
                return;
            }

            await Task.Delay(50, cancellationToken);
        }

        throw new TimeoutException("The delivery did not reach the succeeded state.");
    }

    private static async Task<(int EventCount, int DeliveryCount)> ReadRecordCountsAsync(
        string connectionString)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                (SELECT COUNT(*) FROM [IntegrationEventBus].[Events]),
                (SELECT COUNT(*) FROM [IntegrationEventBus].[Deliveries]);
            """;
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return (reader.GetInt32(0), reader.GetInt32(1));
    }

    private sealed record TestEvent(int Value);

    private sealed class HandlerProbe
    {
        public TaskCompletionSource<TestEvent> Handled { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class TestEventHandler(HandlerProbe probe) : IIntegrationEventHandler<TestEvent>
    {
        public ValueTask HandleAsync(
            TestEvent integrationEvent,
            IntegrationEventContext context,
            CancellationToken cancellationToken)
        {
            probe.Handled.TrySetResult(integrationEvent);
            return ValueTask.CompletedTask;
        }
    }
}
