using System.Collections.ObjectModel;

namespace IntegrationEventBus.Topology;

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

    /// <summary>Gets all registered integration events.</summary>
    public IReadOnlyCollection<IntegrationEventDefinition> Events => [.. _eventsByType.Values];

    /// <summary>Gets all registered subscriptions.</summary>
    public IReadOnlyCollection<SubscriptionDefinition> Subscriptions => [.. _subscriptionsByName.Values];

    /// <summary>Gets the event definition registered for a CLR type.</summary>
    /// <param name="eventType">The registered CLR event type.</param>
    /// <returns>The matching event definition.</returns>
    public IntegrationEventDefinition GetEvent(Type eventType)
    {
        return
            _eventsByType.TryGetValue(eventType, out var definition)
            ? definition
            : throw new InvalidOperationException($"Integration event type '{eventType.FullName}' is not registered.");
    }

    /// <summary>Gets the event definition registered with a stable event name.</summary>
    /// <param name="eventName">The stable event name.</param>
    /// <returns>The matching event definition.</returns>
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

    /// <summary>Gets a subscription by its stable name.</summary>
    /// <param name="subscriptionName">The subscription name.</param>
    /// <returns>The matching subscription definition.</returns>
    public SubscriptionDefinition GetSubscription(string subscriptionName)
    {
        return
            _subscriptionsByName.TryGetValue(subscriptionName, out var definition)
            ? definition
            : throw new InvalidOperationException($"Integration event subscription '{subscriptionName}' is not registered.");
    }

    /// <summary>Gets subscriptions routed to the specified event type and topic.</summary>
    /// <param name="eventType">The registered CLR event type.</param>
    /// <param name="topic">The event topic.</param>
    /// <returns>The matching subscriptions.</returns>
    public IReadOnlyList<SubscriptionDefinition> GetSubscriptions(Type eventType, string topic)
    {
        return [.. _subscriptionsByName.Values.Where(predicate)];

        bool predicate(SubscriptionDefinition subscription)
        {
            return
                string.Equals(subscription.Topic, topic, StringComparison.OrdinalIgnoreCase)
                && subscription.Routes.ContainsKey(eventType);
        }
    }
}
