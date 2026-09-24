using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Jellyfin.Plugin.Mqtt.Players;

/// <summary>
/// Builds the topics and keys used by the bridge.
/// </summary>
public sealed class PlayerTopics
{
    private const string PlayersSegment = "players";

    /// <summary>
    /// Initializes a new instance of the <see cref="PlayerTopics"/> class.
    /// </summary>
    /// <param name="baseTopic">The configured base topic.</param>
    /// <param name="serverId">The Jellyfin server id.</param>
    public PlayerTopics(string baseTopic, string serverId)
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
    /// Gets the filter matching the command topics of every player.
    /// </summary>
    public string CommandFilter => $"{BaseTopic}/{PlayersSegment}/+/command";

    /// <summary>
    /// Gets the filter matching the state topics of every player.
    /// </summary>
    public string StateFilter => $"{BaseTopic}/{PlayersSegment}/+/state";

    /// <summary>
    /// Derives the stable, topic-safe key of a device.
    /// </summary>
    /// <param name="deviceId">The Jellyfin device id.</param>
    /// <returns>The device key.</returns>
    public static string DeviceKey(string deviceId) => Hash(deviceId, 12);

    /// <summary>
    /// Gets the state topic of a player.
    /// </summary>
    /// <param name="key">The device key.</param>
    /// <returns>The topic.</returns>
    public string StateTopic(string key) => $"{BaseTopic}/{PlayersSegment}/{key}/state";

    /// <summary>
    /// Gets the topic carrying the raw artwork of a player.
    /// </summary>
    /// <param name="key">The device key.</param>
    /// <returns>The topic.</returns>
    public string ImageTopic(string key) => $"{BaseTopic}/{PlayersSegment}/{key}/image";

    /// <summary>
    /// Gets the command topic of a player.
    /// </summary>
    /// <param name="key">The device key.</param>
    /// <returns>The topic.</returns>
    public string CommandTopic(string key) => $"{BaseTopic}/{PlayersSegment}/{key}/command";

    /// <summary>
    /// Extracts the device key from a player topic.
    /// </summary>
    /// <param name="topic">A state or command topic.</param>
    /// <param name="suffix">The expected last segment, "state" or "command".</param>
    /// <returns>The device key, or null when the topic does not match.</returns>
    public string? ParseKey(string topic, string suffix)
    {
        var prefix = $"{BaseTopic}/{PlayersSegment}/";
        var end = "/" + suffix;
        if (!topic.StartsWith(prefix, StringComparison.Ordinal) || !topic.EndsWith(end, StringComparison.Ordinal))
        {
            return null;
        }

        var key = topic[prefix.Length..^end.Length];
        return key.Length == 0 || key.Contains('/', StringComparison.Ordinal) ? null : key;
    }

    private static string Hash(string value, int length)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(hash)[..length].ToLower(CultureInfo.InvariantCulture);
    }
}
