using IntegrationEventBus;
using IntegrationEventBus.Internal;
using IntegrationEventBus.Processing;
using IntegrationEventBus.Publishing;
using IntegrationEventBus.Storage;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Provides SQL Server storage registration for the integration event bus.</summary>
public static class SqlServerExtensions
{
    /// <summary>Configures SQL Server as the event and delivery store.</summary>
    /// <param name="builder">The integration event bus builder.</param>
    /// <param name="connectionString">The SQL Server connection string.</param>
    /// <param name="configure">Optionally configures processor timing and command timeout.</param>
    /// <returns>The supplied builder.</returns>
    public static IntegrationEventBusBuilder UseSqlServer(
        this IntegrationEventBusBuilder builder,
        string connectionString,
        Action<EventBusOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        var options = new EventBusOptions
        {
            ConnectionString = connectionString
        };
        configure?.Invoke(options);
        options.Validate();

        builder.Services.AddSingleton(options);
        builder.Services.TryAddSingleton(static provider =>
        {
            return new EventBusMigrator(provider.GetRequiredService<EventBusOptions>());
        });
        builder.Services.TryAddSingleton<IIntegrationEventPublisher, EventPublisher>();
        builder.Services.TryAddSingleton<ISubscriptionRunner, SubscriptionRunner>();

        return builder;
    }
}
