using IntegrationEventBus;
using Microsoft.Extensions.DependencyInjection;

namespace IntegrationEventBus.Tests;

public sealed class TopologyTests
{
    [Fact]
    public void Topology_maps_an_event_to_an_explicit_handler()
    {
        var services = new ServiceCollection();

        var builder = services.AddIntegrationEventBus(topology =>
        {
            topology.Event<OrderPlaced>("orders.placed", "orders");
            topology.Subscription("billing", "orders", subscription =>
                subscription.Handle<OrderPlaced, BillingHandler>());
        });

        var eventDefinition = builder.Topology.GetEvent(typeof(OrderPlaced));
        var subscription = builder.Topology.GetSubscription("billing");

        Assert.Equal("orders.placed", eventDefinition.EventName);
        Assert.Equal(typeof(BillingHandler), subscription.Routes[typeof(OrderPlaced)]);
    }

    [Fact]
    public void Producer_can_subscribe_without_referencing_a_handler()
    {
        var services = new ServiceCollection();

        var builder = services.AddIntegrationEventBus(topology =>
        {
            topology.Event<OrderPlaced>("orders.placed", "orders");
            topology.Subscription("billing", "orders", subscription =>
                subscription.Subscribe<OrderPlaced>());
        });

        var subscription = builder.Topology.GetSubscription("billing");
        Assert.Null(subscription.Routes[typeof(OrderPlaced)]);
    }

    [Fact]
    public void Subscription_and_event_topics_must_match()
    {
        var services = new ServiceCollection();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            services.AddIntegrationEventBus(topology =>
            {
                topology.Event<OrderPlaced>("orders.placed", "orders");
                topology.Subscription("billing", "payments", subscription =>
                    subscription.Subscribe<OrderPlaced>());
            }));

        Assert.Contains("belongs to topic 'orders'", exception.Message);
    }

    private sealed record OrderPlaced(Guid OrderId);

    private sealed class BillingHandler : IIntegrationEventHandler<OrderPlaced>
    {
        public ValueTask HandleAsync(
            OrderPlaced integrationEvent,
            IntegrationEventContext context,
            CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }
}
