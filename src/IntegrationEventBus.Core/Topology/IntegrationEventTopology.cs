using System.Collections.ObjectModel;

namespace IntegrationEventBus;

/// <summary>
/// Immutable event and subscription topology shared by publishers and processors.
/// </summary>
public sealed class IntegrationEventTopology
{
    private readonly IReadOnlyDictionary<Type, IntegrationEventDefinition> _eventsByType;
    private readonly IReadOnlyDictionary<string, SubscriptionDefinition> _subscriptionsByName;

    internal IntegrationEventTopology(
        IDictionary<Type, IntegrationEventDefinition> eventsByType,
        IDictionary<string, SubscriptionDefinition> subscriptionsByName)
    {
        _eventsByType = new ReadOnlyDictionary<Type, IntegrationEventDefinition>(
            new Dictionary<Type, IntegrationEventDefinition>(eventsByType));
        _subscriptionsByName = new ReadOnlyDictionary<string, SubscriptionDefinition>(
            new Dictionary<string, SubscriptionDefinition>(subscriptionsByName, StringComparer.OrdinalIgnoreCase));
    }

    public IReadOnlyCollection<IntegrationEventDefinition> Events => _eventsByType.Values.ToArray();

    public IReadOnlyCollection<SubscriptionDefinition> Subscriptions => _subscriptionsByName.Values.ToArray();

    public IntegrationEventDefinition GetEvent(Type eventType) =>
        _eventsByType.TryGetValue(eventType, out var definition)
            ? definition
            : throw new InvalidOperationException(
                $"Integration event type '{eventType.FullName}' is not registered.");

    public IntegrationEventDefinition GetEvent(string eventName)
    {
        foreach (var definition in _eventsByType.Values)
        {
            if (string.Equals(definition.EventName, eventName, StringComparison.OrdinalIgnoreCase))
            {
                return definition;
            }
        }

        throw new InvalidOperationException($"Integration event name '{eventName}' is not registered.");
    }

    public SubscriptionDefinition GetSubscription(string subscriptionName) =>
        _subscriptionsByName.TryGetValue(subscriptionName, out var definition)
            ? definition
            : throw new InvalidOperationException(
                $"Integration event subscription '{subscriptionName}' is not registered.");

    public IReadOnlyList<SubscriptionDefinition> GetSubscriptions(
        Type eventType,
        string topic)
    {
        return _subscriptionsByName.Values
            .Where(subscription =>
                string.Equals(subscription.Topic, topic, StringComparison.OrdinalIgnoreCase)
                && subscription.Routes.ContainsKey(eventType))
            .ToArray();
    }
}
