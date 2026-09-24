using Jellyfin.Plugin.Mqtt.Configuration;
using Jellyfin.Plugin.Mqtt.Mqtt;
using Jellyfin.Plugin.Mqtt.Players;

namespace Jellyfin.Plugin.Mqtt.Integrations;

/// <summary>
/// What an integration needs to publish players.
/// </summary>
/// <param name="Connection">The MQTT connection.</param>
/// <param name="Topics">The shared topics and keys.</param>
/// <param name="Configuration">The plugin configuration.</param>
public sealed record IntegrationContext(MqttConnection Connection, BridgeTopics Topics, PluginConfiguration Configuration);
