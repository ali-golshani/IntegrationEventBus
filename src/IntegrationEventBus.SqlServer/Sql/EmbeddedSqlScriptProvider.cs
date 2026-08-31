using System.Collections.Concurrent;
using System.Reflection;
using System.Text;

namespace IntegrationEventBus.SqlServer;

internal sealed class EmbeddedSqlScriptProvider : ISqlScriptProvider
{
    private const string ResourcePrefix = "IntegrationEventBus.SqlServer.Sql";

    private static readonly Assembly ResourceAssembly = typeof(EmbeddedSqlScriptProvider).Assembly;

    private static readonly Dictionary<SqlScript, string> ResourceNames = new()
    {
        [SqlScript.CreateSchema] = $"{ResourcePrefix}.Schema.CreateSchema.sql",
        [SqlScript.InsertEvent] = $"{ResourcePrefix}.Publishing.InsertEvent.sql",
        [SqlScript.InsertDelivery] = $"{ResourcePrefix}.Publishing.InsertDelivery.sql",
        [SqlScript.AcquireSubscriptionLock] = $"{ResourcePrefix}.Processing.AcquireSubscriptionLock.sql",
        [SqlScript.ClaimNextDelivery] = $"{ResourcePrefix}.Processing.ClaimNextDelivery.sql",
        [SqlScript.MarkSucceeded] = $"{ResourcePrefix}.Processing.MarkSucceeded.sql",
        [SqlScript.MarkFailed] = $"{ResourcePrefix}.Processing.MarkFailed.sql",
        [SqlScript.ReleaseCancelledAttempt] = $"{ResourcePrefix}.Processing.ReleaseCancelledAttempt.sql"
    };

    private readonly ConcurrentDictionary<SqlScript, string> _cache = new();

    public string Get(SqlScript script) => _cache.GetOrAdd(script, Load);

    private string Load(SqlScript script)
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

        return reader.ReadToEnd();
    }
}
