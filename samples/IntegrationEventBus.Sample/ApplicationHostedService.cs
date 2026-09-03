using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace IntegrationEventBus.Sample;

internal class ApplicationHostedService(
    IServiceScopeFactory serviceScopeFactory,
    SampleDatabaseOptions databaseOptions) : IHostedService
{
    private readonly IServiceScopeFactory serviceScopeFactory = serviceScopeFactory;
    private readonly SampleDatabaseOptions databaseOptions = databaseOptions;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = serviceScopeFactory.CreateScope();
        await ProducerExample.PublishAsync(
            scope.ServiceProvider,
            databaseOptions.ConnectionString,
            new OrderPlaced(10, 1000),
            cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
