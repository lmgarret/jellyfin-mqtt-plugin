using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Mqtt.Mqtt;
using Jellyfin.Plugin.Mqtt.Players;

namespace Jellyfin.Plugin.Mqtt.HomeAssistant;

/// <summary>
/// Announces players to Home Assistant through the discovery format of the
/// <c>mqtt_universal_media_player</c> custom integration.
/// </summary>
public class MediaPlayerDiscoveryPublisher : IDiscoveryPublisher
{
    private const string Platform = "mqtt_universal_media_player";
    private const string Component = "media_player";

    private readonly MqttConnection _connection;

    /// <summary>
    /// Initializes a new instance of the <see cref="MediaPlayerDiscoveryPublisher"/> class.
    /// </summary>
    /// <param name="connection">The MQTT connection.</param>
    public MediaPlayerDiscoveryPublisher(MqttConnection connection)
    {
        _connection = connection;
    }

    /// <inheritdoc />
    public string? GetCleanupFilter(DiscoverySettings config) => $"{Prefix(config)}/{Component}/+/config";

    /// <inheritdoc />
    public Task PublishAsync(DiscoverySettings config, ExposedDevice device, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(device);

        var objectId = ObjectId(config, device.Key);
        var payload = new Dictionary<string, object?>
        {
            ["platform"] = Platform,
            ["unique_id"] = objectId,
            ["state_topic"] = config.Topics.StateTopic(device.Key),
            ["command_topic"] = config.Topics.CommandTopic(device.Key),
            ["availability_topic"] = config.Topics.StatusTopic,
            ["payload_available"] = MqttConnection.OnlinePayload,
            ["payload_not_available"] = MqttConnection.OfflinePayload,
            ["device"] = new Dictionary<string, object?>
            {
                ["identifiers"] = new[] { objectId },
                ["name"] = device.Name,
                ["manufacturer"] = "Jellyfin",
                ["model"] = device.AppName,
                ["sw_version"] = device.AppVersion,
            },
            ["commands"] = new Dictionary<string, object>
            {
                ["play"] = Fixed(PlayerCommands.Play),
                ["pause"] = Fixed(PlayerCommands.Pause),
                ["play_pause"] = Fixed(PlayerCommands.PlayPause),
                ["stop"] = Fixed(PlayerCommands.Stop),
                ["next"] = Fixed(PlayerCommands.Next),
                ["previous"] = Fixed(PlayerCommands.Previous),
                ["volume_set"] = new { key = PlayerCommands.Volume, min = 0, max = 100, step = 1 },
                ["mute"] = new { key = PlayerCommands.Mute },
            },
        };

        return _connection.PublishAsync(DiscoveryTopic(config, objectId), JsonSerializer.Serialize(payload), true, cancellationToken);
    }

    /// <inheritdoc />
    public Task RemoveAsync(DiscoverySettings config, string deviceKey, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(config);
        return _connection.PublishAsync(DiscoveryTopic(config, ObjectId(config, deviceKey)), string.Empty, true, cancellationToken);
    }

    /// <inheritdoc />
    public Task CleanupAsync(DiscoverySettings config, string topic, string payload, IReadOnlySet<string> exposedKeys, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(topic);
        ArgumentNullException.ThrowIfNull(exposedKeys);

        var prefix = $"{Prefix(config)}/{Component}/";
        var ownPrefix = ObjectId(config, string.Empty);
        if (string.IsNullOrEmpty(payload) || !topic.StartsWith(prefix, StringComparison.Ordinal) || !topic.EndsWith("/config", StringComparison.Ordinal))
        {
            return Task.CompletedTask;
        }

        var objectId = topic[prefix.Length..^"/config".Length];
        if (!objectId.StartsWith(ownPrefix, StringComparison.Ordinal) || exposedKeys.Contains(objectId[ownPrefix.Length..]))
        {
            return Task.CompletedTask;
        }

        return _connection.PublishAsync(topic, string.Empty, true, cancellationToken);
    }

    private static object Fixed(string key) => new { key, value = true };

    private static string Prefix(DiscoverySettings config) =>
        string.IsNullOrWhiteSpace(config.Prefix) ? "homeassistant" : config.Prefix.Trim().TrimEnd('/');

    private static string ObjectId(DiscoverySettings config, string deviceKey) => $"jellyfin_{config.Topics.ServerKey}_{deviceKey}";

    private static string DiscoveryTopic(DiscoverySettings config, string objectId) => $"{Prefix(config)}/{Component}/{objectId}/config";
}
