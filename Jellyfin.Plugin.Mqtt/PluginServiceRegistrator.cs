using Jellyfin.Plugin.Mqtt.HomeAssistant;
using Jellyfin.Plugin.Mqtt.Mqtt;
using Jellyfin.Plugin.Mqtt.Players;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.Mqtt;

/// <summary>
/// Registers the plugin services.
/// </summary>
public class PluginServiceRegistrator : IPluginServiceRegistrator
{
    /// <inheritdoc />
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddSingleton<MqttConnection>();
        serviceCollection.AddSingleton<ArtworkLoader>();
        serviceCollection.AddSingleton<IDiscoveryPublisher, MediaPlayerDiscoveryPublisher>();
        serviceCollection.AddHostedService<PlayerBridgeService>();
    }
}
