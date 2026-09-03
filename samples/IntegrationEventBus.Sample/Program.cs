using IntegrationEventBus.Sample;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddIntegrationEventBus();

builder.Services.AddHostedService<ApplicationHostedService>();

var app = builder.Build();

//await app.Services.GetRequiredService<EventBusMigrator>().MigrateAsync();

await app.RunAsync();
