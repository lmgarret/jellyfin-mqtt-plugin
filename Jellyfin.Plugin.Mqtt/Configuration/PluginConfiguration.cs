using System;
using System.Diagnostics.CodeAnalysis;
using Jellyfin.Plugin.Mqtt.Integrations.UniversalMediaPlayer;
using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.Mqtt.Configuration;

/// <summary>
/// Plugin configuration.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Gets or sets the MQTT broker host name. The bridge stays idle while empty.
    /// </summary>
    public string BrokerHost { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the MQTT broker port.
    /// </summary>
    public int BrokerPort { get; set; } = 1883;

    /// <summary>
    /// Gets or sets a value indicating whether to connect to the broker using TLS.
    /// </summary>
    public bool UseTls { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether invalid broker certificates are accepted.
    /// </summary>
    public bool AllowUntrustedCertificates { get; set; }

    /// <summary>
    /// Gets or sets the MQTT user name.
    /// </summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the MQTT password.
    /// </summary>
    public string Password { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the MQTT client id.
    /// </summary>
    public string ClientId { get; set; } = "jellyfin";

    /// <summary>
    /// Gets or sets the topic prefix under which player state and commands live.
    /// </summary>
    public string BaseTopic { get; set; } = "jellyfin";

    /// <summary>
    /// Gets or sets the ids of the enabled integrations, which decide the MQTT formats players are published in.
    /// </summary>
    [SuppressMessage("Performance", "CA1819:Properties should not return arrays", Justification = "Required by the XML serializer.")]
    public string[] EnabledIntegrations { get; set; } = [UniversalMediaPlayerIntegration.IntegrationId];

    /// <summary>
    /// Gets or sets the Home Assistant discovery prefix, used by the mqtt_universal_media_player integration.
    /// </summary>
    public string DiscoveryPrefix { get; set; } = "homeassistant";

    /// <summary>
    /// Gets or sets the Jellyfin URL reachable by MQTT consumers, used for the artwork link in the state.
    /// The link is omitted while empty; the artwork itself is always published on the image topic.
    /// </summary>
    public string ServerUrl { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the ids of the users whose devices are exposed. No user is exposed while empty.
    /// </summary>
    [SuppressMessage("Performance", "CA1819:Properties should not return arrays", Justification = "Required by the XML serializer.")]
    public string[] AllowedUserIds { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Gets or sets the ids of the exposed devices, among those of the allowed users. No device is exposed while empty.
    /// </summary>
    [SuppressMessage("Performance", "CA1819:Properties should not return arrays", Justification = "Required by the XML serializer.")]
    public string[] ExposedDeviceIds { get; set; } = Array.Empty<string>();
}
