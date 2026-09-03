using System.Text.Json;
using IntegrationEventBus.Topology;
using Microsoft.Extensions.DependencyInjection;

namespace IntegrationEventBus;

/// <summary>
/// Provides provider and hosting extensions with access to the configured service collection.
/// </summary>
public sealed class IntegrationEventBusBuilder
{
    internal IntegrationEventBusBuilder(
        IServiceCollection services,
        IntegrationEventTopology topology,
        JsonSerializerOptions serializerOptions)
    {
        Services = services;
        Topology = topology;
        SerializerOptions = serializerOptions;
    }

    /// <summary>Gets the service collection being configured.</summary>
    public IServiceCollection Services { get; }

    /// <summary>Gets the immutable event and subscription topology.</summary>
    public IntegrationEventTopology Topology { get; }

    /// <summary>Gets the JSON serializer options used for event payloads.</summary>
    public JsonSerializerOptions SerializerOptions { get; }
}
