using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Jellyfin.Plugin.Mqtt.Players;

/// <summary>
/// The topics and keys shared by every integration.
/// </summary>
public sealed class BridgeTopics
{
    /// <summary>
    /// Initializes a new instance of the <see cref="BridgeTopics"/> class.
    /// </summary>
    /// <param name="baseTopic">The configured base topic.</param>
    /// <param name="serverId">The Jellyfin server id.</param>
    public BridgeTopics(string baseTopic, string serverId)
    {
        BaseTopic = string.IsNullOrWhiteSpace(baseTopic) ? "jellyfin" : baseTopic.Trim().TrimEnd('/');
        ServerKey = Hash(serverId, 8);
    }

    /// <summary>
    /// Gets the base topic.
    /// </summary>
    public string BaseTopic { get; }

    /// <summary>
    /// Gets a short key identifying this server, so several servers can share a broker.
    /// </summary>
    public string ServerKey { get; }

    /// <summary>
    /// Gets the topic carrying the bridge availability.
    /// </summary>
    public string StatusTopic => $"{BaseTopic}/status";

    /// <summary>
    /// Derives the stable, topic-safe key of a device.
    /// </summary>
    /// <param name="deviceId">The Jellyfin device id.</param>
    /// <returns>The device key.</returns>
    public static string DeviceKey(string deviceId) => Hash(deviceId, 12);

    private static string Hash(string value, int length)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(hash)[..length].ToLower(CultureInfo.InvariantCulture);
    }
}
