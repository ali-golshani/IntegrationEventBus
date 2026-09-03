namespace IntegrationEventBus;

/// <summary>
/// Configures SQL Server storage and processor timing.
/// </summary>
public sealed class EventBusOptions
{
    /// <summary>Gets or sets how long an idle processor waits before polling for new deliveries.</summary>
    public TimeSpan PollingInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Gets or sets how long a processor waits before retrying a subscription lock.</summary>
    public TimeSpan LockRetryInterval { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>Gets or sets the SQL command timeout, in seconds.</summary>
    public int CommandTimeoutSeconds { get; set; } = 30;
    internal string ConnectionString { get; set; } = string.Empty;

    internal void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ConnectionString);

        if (PollingInterval <= TimeSpan.Zero)
        {
            throw new InvalidOperationException("Polling interval must be positive.");
        }

        if (LockRetryInterval <= TimeSpan.Zero)
        {
            throw new InvalidOperationException("Lock retry interval must be positive.");
        }

        if (CommandTimeoutSeconds < 1)
        {
            throw new InvalidOperationException("Command timeout must be positive.");
        }
    }
}
