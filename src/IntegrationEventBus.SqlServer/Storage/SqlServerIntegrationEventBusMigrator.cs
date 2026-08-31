using Microsoft.Data.SqlClient;

namespace IntegrationEventBus.SqlServer;

/// <summary>
/// Creates the SQL Server objects required by the integration event bus.
/// </summary>
public sealed class SqlServerIntegrationEventBusMigrator
{
    private static readonly string CreateSchemaQuery = Properties.Resources.CreateSchema;

    private readonly SqlServerIntegrationEventBusOptions _options;

    internal SqlServerIntegrationEventBusMigrator(SqlServerIntegrationEventBusOptions options)
    {
        _options = options;
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
        command.CommandText = CreateSchemaQuery;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
