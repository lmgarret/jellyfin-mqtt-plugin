using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Mqtt.Configuration;
using Jellyfin.Plugin.Mqtt.Integrations.UniversalMediaPlayer;
using Jellyfin.Plugin.Mqtt.Mqtt;
using Jellyfin.Plugin.Mqtt.Players;

namespace Jellyfin.Plugin.Mqtt.Integrations.HassMqttMediaPlayer;

/// <summary>
/// Exposes players to Home Assistant for the
/// <see href="https://github.com/TroyFernandes/hass-mqtt-mediaplayer">hass-mqtt-mediaplayer</see> custom integration.
/// </summary>
/// <remarks>
/// That integration has no discovery: its media players are declared in YAML, from templates over
/// Home Assistant entities. Each player is therefore announced as a native MQTT sensor, whose state
/// is the playback status and whose attributes hold the metadata the templates read.
/// Topics, under the base topic:
/// <list type="bullet">
/// <item><c>mqtt_mediaplayer/&lt;key&gt;/state</c>: retained JSON state, the sensor state and attributes.</item>
/// <item><c>mqtt_mediaplayer/&lt;key&gt;/albumart</c>: retained base64 artwork, empty when there is none.</item>
/// <item><c>mqtt_mediaplayer/&lt;key&gt;/command</c>: JSON commands, as for <c>mqtt_universal_media_player</c>.</item>
/// </list>
/// Sensors are announced on <c>&lt;discovery prefix&gt;/sensor/jellyfin_&lt;server&gt;_&lt;key&gt;/config</c>.
/// </remarks>
public class HassMqttMediaPlayerIntegration : IPlayerIntegration
{
    /// <summary>
    /// The integration id, the domain of the consumer.
    /// </summary>
    public const string IntegrationId = "mqtt-mediaplayer";

    private const string PlayersSegment = "mqtt_mediaplayer";
    private const string StateSuffix = "state";
    private const string AlbumArtSuffix = "albumart";
    private const string CommandSuffix = "command";
    private const string Component = "sensor";

    /// <inheritdoc />
    public string Id => IntegrationId;

    /// <inheritdoc />
    public string Name => "Home Assistant: hass-mqtt-mediaplayer";

    /// <inheritdoc />
    public string Description => "Requires the mqtt-mediaplayer custom integration. Players are discovered as sensors; each media player is then declared in YAML from its sensor.";

    /// <inheritdoc />
    public Uri RepositoryUrl { get; } = new("https://github.com/TroyFernandes/hass-mqtt-mediaplayer");

    /// <inheritdoc />
    public IReadOnlyList<IntegrationSetting> Settings { get; } =
    [
        new(nameof(PluginConfiguration.DiscoveryPrefix), "Home Assistant discovery prefix", null),
    ];

    /// <inheritdoc />
    public IEnumerable<string> GetSubscriptions(IntegrationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return [PlayerTopic(context, "+", CommandSuffix)];
    }

    /// <inheritdoc />
    public IEnumerable<string> GetCleanupFilters(IntegrationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return [$"{DiscoveryPrefix(context)}/{Component}/+/config", PlayerTopic(context, "+", StateSuffix)];
    }

    /// <inheritdoc />
    public Task PublishDeviceAsync(IntegrationContext context, ExposedDevice device, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(device);

        var objectId = ObjectId(context, device.Key);
        var stateTopic = PlayerTopic(context, device.Key, StateSuffix);
        var payload = new Dictionary<string, object?>
        {
            ["name"] = null,
            ["unique_id"] = objectId,
            ["icon"] = "mdi:play-network",
            ["state_topic"] = stateTopic,
            ["value_template"] = "{{ value_json.state }}",
            ["json_attributes_topic"] = stateTopic,
            ["availability_topic"] = context.Topics.StatusTopic,
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
        };

        return context.Connection.PublishAsync(DiscoveryTopic(context, objectId), JsonSerializer.Serialize(payload), true, cancellationToken);
    }

    /// <inheritdoc />
    public async Task PublishStateAsync(IntegrationContext context, PlayerUpdate update, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(update);

        var key = update.Device.Key;

        // The artwork goes out before the state, so the card never shows new metadata with old artwork.
        if (update.ImageChanged)
        {
            var image = await update.GetImageAsync(cancellationToken).ConfigureAwait(false);
            await context.Connection.PublishAsync(PlayerTopic(context, key, AlbumArtSuffix), image is null ? string.Empty : Convert.ToBase64String(image), true, cancellationToken).ConfigureAwait(false);
        }

        await context.Connection.PublishAsync(PlayerTopic(context, key, StateSuffix), SerializeState(context, key, update.State), true, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task RemoveDeviceAsync(IntegrationContext context, string deviceKey, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        await context.Connection.PublishAsync(DiscoveryTopic(context, ObjectId(context, deviceKey)), string.Empty, true, cancellationToken).ConfigureAwait(false);
        await ClearPlayerAsync(context, deviceKey, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<PlayerCommands?> HandleMessageAsync(IntegrationContext context, string topic, string payload, IReadOnlySet<string> exposedKeys, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(topic);
        ArgumentNullException.ThrowIfNull(exposedKeys);

        if (ParsePlayerKey(context, topic, CommandSuffix) is { } commandKey)
        {
            return new PlayerCommands(commandKey, UniversalMediaPlayerIntegration.ParseCommands(payload));
        }

        // Cleanup of retained messages left by players that are no longer exposed.
        if (string.IsNullOrEmpty(payload))
        {
            return null;
        }

        if (ParsePlayerKey(context, topic, StateSuffix) is { } stateKey)
        {
            if (!exposedKeys.Contains(stateKey))
            {
                await ClearPlayerAsync(context, stateKey, cancellationToken).ConfigureAwait(false);
            }

            return null;
        }

        var discoveryPrefix = $"{DiscoveryPrefix(context)}/{Component}/";
        var ownPrefix = ObjectId(context, string.Empty);
        if (topic.StartsWith(discoveryPrefix, StringComparison.Ordinal) && topic.EndsWith("/config", StringComparison.Ordinal))
        {
            var objectId = topic[discoveryPrefix.Length..^"/config".Length];
            if (objectId.StartsWith(ownPrefix, StringComparison.Ordinal) && !exposedKeys.Contains(objectId[ownPrefix.Length..]))
            {
                await context.Connection.PublishAsync(topic, string.Empty, true, cancellationToken).ConfigureAwait(false);
            }
        }

        return null;
    }

    /// <summary>
    /// Serializes a state. Every key is always present, <c>null</c> when not applicable, and the
    /// player's own topics are included so the YAML templates only need the sensor. The position
    /// and duration are left out: the consumer does not use them, and they would make Home Assistant
    /// record the sensor every few seconds.
    /// </summary>
    /// <param name="context">The integration context.</param>
    /// <param name="key">The device key.</param>
    /// <param name="state">The state.</param>
    /// <returns>The JSON payload.</returns>
    public static string SerializeState(IntegrationContext context, string key, PlayerState state)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(state);

        return JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["state"] = state.Status.ToString().ToLowerInvariant(),
            ["volume"] = state.Volume,
            ["muted"] = state.Muted,
            ["media_id"] = state.MediaId,
            ["media_title"] = state.MediaTitle,

            // The consumer has no series field: show it in place of the artist, as Home Assistant cards do.
            ["media_artist"] = state.MediaArtist ?? state.MediaSeriesTitle,
            ["media_album_name"] = state.MediaAlbumName,
            ["media_series_title"] = state.MediaSeriesTitle,
            ["media_season"] = state.MediaSeason,
            ["media_episode"] = state.MediaEpisode,
            ["media_content_type"] = state.MediaContentType,
            ["media_image_url"] = state.MediaImageUrl,
            ["app_name"] = state.AppName,
            ["device_name"] = state.DeviceName,
            ["user"] = state.UserName,
            ["command_topic"] = PlayerTopic(context, key, CommandSuffix),
            ["albumart_topic"] = PlayerTopic(context, key, AlbumArtSuffix),
        });
    }

    private static string PlayerTopic(IntegrationContext context, string key, string suffix) =>
        $"{context.Topics.BaseTopic}/{PlayersSegment}/{key}/{suffix}";

    private static string? ParsePlayerKey(IntegrationContext context, string topic, string suffix)
    {
        var prefix = $"{context.Topics.BaseTopic}/{PlayersSegment}/";
        var end = "/" + suffix;
        if (!topic.StartsWith(prefix, StringComparison.Ordinal) || !topic.EndsWith(end, StringComparison.Ordinal) || topic.Length <= prefix.Length + end.Length)
        {
            return null;
        }

        var key = topic[prefix.Length..^end.Length];
        return key.Contains('/', StringComparison.Ordinal) ? null : key;
    }

    private static async Task ClearPlayerAsync(IntegrationContext context, string key, CancellationToken cancellationToken)
    {
        await context.Connection.PublishAsync(PlayerTopic(context, key, StateSuffix), string.Empty, true, cancellationToken).ConfigureAwait(false);
        await context.Connection.PublishAsync(PlayerTopic(context, key, AlbumArtSuffix), string.Empty, true, cancellationToken).ConfigureAwait(false);
    }

    private static string DiscoveryPrefix(IntegrationContext context)
    {
        var prefix = context.Configuration.DiscoveryPrefix;
        return string.IsNullOrWhiteSpace(prefix) ? "homeassistant" : prefix.Trim().TrimEnd('/');
    }

    private static string ObjectId(IntegrationContext context, string deviceKey) => $"jellyfin_{context.Topics.ServerKey}_{deviceKey}";

    private static string DiscoveryTopic(IntegrationContext context, string objectId) => $"{DiscoveryPrefix(context)}/{Component}/{objectId}/config";
}
