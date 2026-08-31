using Microsoft.Data.SqlClient;

namespace IntegrationEventBus.SqlServer;

/// <summary>
/// Creates the SQL Server objects required by the integration event bus.
/// </summary>
public sealed class SqlServerIntegrationEventBusMigrator
{
    private readonly SqlServerIntegrationEventBusOptions _options;
    private readonly ISqlScriptProvider _scripts;

    internal SqlServerIntegrationEventBusMigrator(
        SqlServerIntegrationEventBusOptions options,
        ISqlScriptProvider scripts)
    {
        _options = options;
        _scripts = scripts;
    }

    /// <summary>
    /// Creates any missing schema and tables required by the integration event bus.
    /// </summary>
    public async Task MigrateAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqlConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandTimeout = _options.CommandTimeoutSeconds;
        command.CommandText = _scripts.Get(SqlScript.CreateSchema);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
