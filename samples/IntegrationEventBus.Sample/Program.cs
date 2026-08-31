using IntegrationEventBus;
using IntegrationEventBus.Sample;
using IntegrationEventBus.SqlServer;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

const string connectionString =
    "Server=.;Database=IntegrationEventBusSample;User Id=golshani;Password=Ali_Golshani;TrustServerCertificate=True;";

var builder = Host.CreateApplicationBuilder(args);

builder.Services
    .AddIntegrationEventBus(topology =>
    {
        topology.Event<OrderPlaced>("orders.placed", "orders");
        topology.Subscription("billing", "orders", subscription =>
        {
            subscription.Handle<OrderPlaced, OrderPlacedHandler>();
            subscription.UseRetryPolicy(new RetryPolicy
            {
                Name = "external-api",
                Version = 1,
                ImmediateRetryCount = 3,
                ImmediateRetryDelay = TimeSpan.FromSeconds(5),
                DeferredRetryDelay = TimeSpan.FromMinutes(15),
                MaxAttempts = 20,
                DeadLetterAfter = TimeSpan.FromHours(6)
            });
        });
    })
    .UseSqlServer(connectionString)
    .AddHostedProcessor();

builder.Services.AddHostedService<ApplicationHostedService>();

var app = builder.Build();

//await app.Services.GetRequiredService<SqlServerIntegrationEventBusMigrator>().MigrateAsync();

await app.RunAsync();
