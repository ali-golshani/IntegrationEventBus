using IntegrationEventBus.Infrastructure;
using Microsoft.Data.SqlClient;

namespace IntegrationEventBus.SqlServer;

internal sealed class SqlServerStoreInitializer(
    SqlServerIntegrationEventBusOptions options,
    ISqlScriptProvider scripts)
    : IIntegrationEventStoreInitializer
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private volatile bool _initialized;

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        if (_initialized || !options.AutoCreateSchema)
        {
            return;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_initialized)
            {
                return;
            }

            await using var connection = new SqlConnection(options.ConnectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandTimeout = options.CommandTimeoutSeconds;
            command.CommandText = scripts.Get(SqlScript.CreateSchema);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            _initialized = true;
        }
        finally
        {
            _gate.Release();
        }
    }

}
