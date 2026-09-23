using System;
using System.Diagnostics.CodeAnalysis;
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
    /// Gets or sets the Home Assistant discovery prefix.
    /// </summary>
    public string DiscoveryPrefix { get; set; } = "homeassistant";

    /// <summary>
    /// Gets or sets the Jellyfin URL reachable by MQTT consumers, used for artwork links.
    /// Artwork is not published while empty.
    /// </summary>
    public string ServerUrl { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the ids of the users whose devices are exposed. No user is exposed while empty.
    /// </summary>
    [SuppressMessage("Performance", "CA1819:Properties should not return arrays", Justification = "Required by the XML serializer.")]
    public string[] AllowedUserIds { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Gets or sets the ids of devices that are never exposed, even for allowed users.
    /// </summary>
    [SuppressMessage("Performance", "CA1819:Properties should not return arrays", Justification = "Required by the XML serializer.")]
    public string[] ExcludedDeviceIds { get; set; } = Array.Empty<string>();
}
