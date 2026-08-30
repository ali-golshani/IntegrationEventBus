namespace IntegrationEventBus.Infrastructure;

public interface IProcessorSignal
{
    void Pulse();

    Task WaitAsync(TimeSpan timeout, CancellationToken cancellationToken);
}
