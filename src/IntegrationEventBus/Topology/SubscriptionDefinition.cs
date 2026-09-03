using System.Collections.ObjectModel;

namespace IntegrationEventBus.Topology;

/// <summary>
/// Describes one independent consumer of a topic.
/// </summary>
public sealed class SubscriptionDefinition
{
    private readonly IReadOnlyDictionary<Type, Type?> _routes;

    internal SubscriptionDefinition(
        string name,
        string topic,
        RetryPolicy retryPolicy,
        IDictionary<Type, Type?> routes)
    {
        Name = name;
        Topic = topic;
        RetryPolicy = retryPolicy;
        _routes = new ReadOnlyDictionary<Type, Type?>(new Dictionary<Type, Type?>(routes));
    }

    /// <summary>Gets the stable subscription name.</summary>
    public string Name { get; }

    /// <summary>Gets the subscribed topic.</summary>
    public string Topic { get; }

    /// <summary>Gets the retry policy applied to failed deliveries.</summary>
    public RetryPolicy RetryPolicy { get; }

    /// <summary>
    /// Gets subscribed CLR event types and their optional handler types. A producer can register a
    /// subscription route without referencing the consumer's handler assembly.
    /// </summary>
    public IReadOnlyDictionary<Type, Type?> Routes => _routes;

    /// <summary>Gets whether this process registered at least one local handler.</summary>
    public bool HasHandlers => _routes.Values.Any(static handlerType => handlerType is not null);
}
