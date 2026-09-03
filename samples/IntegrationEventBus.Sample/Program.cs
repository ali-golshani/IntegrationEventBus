using IntegrationEventBus;
using IntegrationEventBus.Sample;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("IntegrationEventBus")
    ?? throw new InvalidOperationException(
        "Connection string 'IntegrationEventBus' is not configured.");

builder.Services.AddSingleton(new SampleDatabaseOptions(connectionString));
builder.Services.AddIntegrationEventBus(connectionString);

builder.Services.AddHostedService<ApplicationHostedService>();

var app = builder.Build();

await app.Services.GetRequiredService<EventBusMigrator>().MigrateAsync();

await app.RunAsync();
