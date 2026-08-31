using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace IntegrationEventBus.Sample;

internal class ApplicationHostedService(IServiceScopeFactory serviceScopeFactory) : IHostedService
{
    const string connectionString =
        "Server=.;Database=IntegrationEventBusSample;User Id=golshani;Password=Ali_Golshani;TrustServerCertificate=True;";
   
    private readonly IServiceScopeFactory serviceScopeFactory = serviceScopeFactory;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = serviceScopeFactory.CreateScope();
        await ProducerExample.PublishAsync(scope.ServiceProvider, connectionString, new OrderPlaced(10, 1000), default);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
