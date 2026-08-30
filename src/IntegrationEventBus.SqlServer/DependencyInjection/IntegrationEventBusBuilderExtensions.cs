using IntegrationEventBus;
using IntegrationEventBus.Infrastructure;
using IntegrationEventBus.SqlServer;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Microsoft.Extensions.DependencyInjection;

public static class SqlServerIntegrationEventBusBuilderExtensions
{
    public static IntegrationEventBusBuilder UseSqlServer(
        this IntegrationEventBusBuilder builder,
        string connectionString,
        Action<SqlServerIntegrationEventBusOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        var options = new SqlServerIntegrationEventBusOptions
        {
            ConnectionString = connectionString
        };
        configure?.Invoke(options);
        options.Validate();

        builder.Services.AddSingleton(options);
        builder.Services.TryAddSingleton<ISqlScriptProvider, EmbeddedSqlScriptProvider>();
        builder.Services.TryAddSingleton<SqlServerStoreInitializer>();
        builder.Services.TryAddSingleton<IIntegrationEventStoreInitializer>(
            static provider => provider.GetRequiredService<SqlServerStoreInitializer>());
        builder.Services.TryAddSingleton<IIntegrationEventPublisher, SqlServerIntegrationEventPublisher>();
        builder.Services.TryAddSingleton<ISubscriptionRunner, SqlServerSubscriptionRunner>();

        return builder;
    }
}
