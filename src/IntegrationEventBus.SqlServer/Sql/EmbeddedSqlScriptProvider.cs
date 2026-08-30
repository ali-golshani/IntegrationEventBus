using System.Collections.Concurrent;
using System.Reflection;
using System.Text;

namespace IntegrationEventBus.SqlServer;

internal sealed class EmbeddedSqlScriptProvider(SqlServerIntegrationEventBusOptions options)
    : ISqlScriptProvider
{
    private const string ResourcePrefix = "IntegrationEventBus.SqlServer.Sql";

    private static readonly Assembly ResourceAssembly = typeof(EmbeddedSqlScriptProvider).Assembly;

    private static readonly IReadOnlyDictionary<SqlScript, string> ResourceNames =
        new Dictionary<SqlScript, string>
        {
            [SqlScript.CreateSchema] = $"{ResourcePrefix}.Schema.CreateSchema.sql",
            [SqlScript.InsertEvent] = $"{ResourcePrefix}.Publishing.InsertEvent.sql",
            [SqlScript.InsertDelivery] = $"{ResourcePrefix}.Publishing.InsertDelivery.sql",
            [SqlScript.AcquireSubscriptionLock] =
                $"{ResourcePrefix}.Processing.AcquireSubscriptionLock.sql",
            [SqlScript.ClaimNextDelivery] =
                $"{ResourcePrefix}.Processing.ClaimNextDelivery.sql",
            [SqlScript.MarkSucceeded] = $"{ResourcePrefix}.Processing.MarkSucceeded.sql",
            [SqlScript.MarkFailed] = $"{ResourcePrefix}.Processing.MarkFailed.sql",
            [SqlScript.ReleaseCancelledAttempt] =
                $"{ResourcePrefix}.Processing.ReleaseCancelledAttempt.sql"
        };

    private readonly ConcurrentDictionary<SqlScript, string> _cache = new();

    public string Get(SqlScript script) => _cache.GetOrAdd(script, LoadAndPrepare);

    private string LoadAndPrepare(SqlScript script)
    {
        if (!ResourceNames.TryGetValue(script, out var resourceName))
        {
            throw new ArgumentOutOfRangeException(nameof(script), script, "Unknown SQL script.");
        }

        using var stream = ResourceAssembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException(
                $"Embedded SQL resource '{resourceName}' could not be found.");
        using var reader = new StreamReader(
            stream,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true);

        var commandText = reader.ReadToEnd()
            .Replace("{{Schema}}", SqlIdentifier.Quote(options.SchemaName), StringComparison.Ordinal)
            .Replace("{{SchemaName}}", options.SchemaName, StringComparison.Ordinal);

        if (commandText.Contains("{{", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Embedded SQL resource '{resourceName}' contains an unresolved template token.");
        }

        return commandText;
    }
}
