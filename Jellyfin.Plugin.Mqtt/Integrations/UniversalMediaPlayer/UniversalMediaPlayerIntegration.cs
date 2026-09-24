using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Mqtt.Mqtt;
using Jellyfin.Plugin.Mqtt.Players;

namespace Jellyfin.Plugin.Mqtt.Integrations.UniversalMediaPlayer;

/// <summary>
/// Exposes players to Home Assistant through the
/// <see href="https://github.com/grzegorz914/homeassistant-mqtt-media-player">mqtt_universal_media_player</see>
/// custom integration (0.3.0 or later).
/// </summary>
/// <remarks>
/// Topics, under the base topic:
/// <list type="bullet">
/// <item><c>players/&lt;key&gt;/state</c>: retained JSON state, every key always present.</item>
/// <item><c>players/&lt;key&gt;/image</c>: retained artwork bytes, empty when there is none.</item>
/// <item><c>players/&lt;key&gt;/command</c>: JSON commands such as <c>{"pause": true}</c> or <c>{"volume": 40}</c>.</item>
/// </list>
/// Players are announced on <c>&lt;discovery prefix&gt;/media_player/jellyfin_&lt;server&gt;_&lt;key&gt;/config</c>.
/// </remarks>
public class UniversalMediaPlayerIntegration : IPlayerIntegration
{
    /// <summary>
    /// The integration id.
    /// </summary>
    public const string IntegrationId = "mqtt_universal_media_player";

    private const string PlayersSegment = "players";
    private const string StateSuffix = "state";
    private const string ImageSuffix = "image";
    private const string CommandSuffix = "command";
    private const string Component = "media_player";

    private static readonly Dictionary<string, PlayerCommandKind> _triggers = new(StringComparer.Ordinal)
    {
        ["play"] = PlayerCommandKind.Play,
        ["pause"] = PlayerCommandKind.Pause,
        ["play_pause"] = PlayerCommandKind.PlayPause,
        ["stop"] = PlayerCommandKind.Stop,
        ["next"] = PlayerCommandKind.Next,
        ["previous"] = PlayerCommandKind.Previous,
    };

    /// <inheritdoc />
    public string Id => IntegrationId;

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
        var payload = new Dictionary<string, object?>
        {
            ["platform"] = IntegrationId,
            ["unique_id"] = objectId,
            ["state_topic"] = PlayerTopic(context, device.Key, StateSuffix),
            ["command_topic"] = PlayerTopic(context, device.Key, CommandSuffix),
            ["image_topic"] = PlayerTopic(context, device.Key, ImageSuffix),
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
            ["commands"] = new Dictionary<string, object>
            {
                ["play"] = Trigger("play"),
                ["pause"] = Trigger("pause"),
                ["play_pause"] = Trigger("play_pause"),
                ["stop"] = Trigger("stop"),
                ["next"] = Trigger("next"),
                ["previous"] = Trigger("previous"),
                ["volume_set"] = new { key = "volume", min = 0, max = 100, step = 1 },
                ["mute"] = new { key = "mute" },
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

        // The image goes out before the state, so the card never shows new metadata with old artwork.
        if (update.ImageChanged)
        {
            var image = await update.GetImageAsync(cancellationToken).ConfigureAwait(false);
            await context.Connection.PublishAsync(PlayerTopic(context, key, ImageSuffix), image ?? [], true, cancellationToken).ConfigureAwait(false);
        }

        await context.Connection.PublishAsync(PlayerTopic(context, key, StateSuffix), SerializeState(update.State), true, cancellationToken).ConfigureAwait(false);
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
            return new PlayerCommands(commandKey, ParseCommands(payload));
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
    /// Serializes a state. Every key is always present, so the integration, which merges
    /// partial updates, drops values that no longer apply.
    /// </summary>
    /// <param name="state">The state.</param>
    /// <returns>The JSON payload.</returns>
    public static string SerializeState(PlayerState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        return JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["state"] = state.Status.ToString().ToLowerInvariant(),
            ["volume"] = state.Volume,
            ["muted"] = state.Muted,
            ["media_id"] = state.MediaId,
            ["media_title"] = state.MediaTitle,
            ["media_artist"] = state.MediaArtist,
            ["media_album_name"] = state.MediaAlbumName,
            ["media_series_title"] = state.MediaSeriesTitle,
            ["media_season"] = state.MediaSeason,
            ["media_episode"] = state.MediaEpisode,
            ["media_content_type"] = state.MediaContentType,
            ["media_image_url"] = state.MediaImageUrl,
            ["media_duration"] = Seconds(state.MediaDuration),
            ["media_position"] = Seconds(state.MediaPosition),
            ["app_name"] = state.AppName,
            ["device_name"] = state.DeviceName,
            ["user"] = state.UserName,
        });
    }

    /// <summary>
    /// Parses a command payload such as <c>{"pause": true, "volume": 40}</c>.
    /// </summary>
    /// <param name="payload">The JSON payload.</param>
    /// <returns>The commands, in order.</returns>
    /// <exception cref="FormatException">The payload is not a valid command.</exception>
    public static IReadOnlyList<PlayerCommand> ParseCommands(string payload)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(payload);
        }
        catch (JsonException ex)
        {
            throw new FormatException("Command payload is not valid JSON", ex);
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new FormatException("Command payload must be a JSON object");
            }

            var commands = new List<PlayerCommand>();
            foreach (var property in document.RootElement.EnumerateObject())
            {
                commands.Add(ParseCommand(property.Name, property.Value));
            }

            return commands;
        }
    }

    private static PlayerCommand ParseCommand(string name, JsonElement value)
    {
        if (_triggers.TryGetValue(name, out var kind))
        {
            return new PlayerCommand(kind);
        }

        return name switch
        {
            "seek" => new PlayerCommand(PlayerCommandKind.Seek, ReadNumber(name, value)),
            "volume" => new PlayerCommand(PlayerCommandKind.SetVolume, ReadNumber(name, value)),
            "mute" => value.ValueKind switch
            {
                JsonValueKind.True => new PlayerCommand(PlayerCommandKind.SetMute, 1),
                JsonValueKind.False => new PlayerCommand(PlayerCommandKind.SetMute, 0),
                _ => throw new FormatException($"Command '{name}' expects a boolean"),
            },
            _ => throw new FormatException($"Unknown command '{name}'"),
        };
    }

    private static double ReadNumber(string name, JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Number)
        {
            return value.GetDouble();
        }

        if (value.ValueKind == JsonValueKind.String
            && double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        throw new FormatException($"Command '{name}' expects a number");
    }

    private static long? Seconds(TimeSpan? value) => value is null ? null : (long)value.Value.TotalSeconds;

    private static object Trigger(string key) => new { key, value = true };

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
        await context.Connection.PublishAsync(PlayerTopic(context, key, ImageSuffix), string.Empty, true, cancellationToken).ConfigureAwait(false);
    }

    private static string DiscoveryPrefix(IntegrationContext context)
    {
        var prefix = context.Configuration.DiscoveryPrefix;
        return string.IsNullOrWhiteSpace(prefix) ? "homeassistant" : prefix.Trim().TrimEnd('/');
    }

    private static string ObjectId(IntegrationContext context, string deviceKey) => $"jellyfin_{context.Topics.ServerKey}_{deviceKey}";

    private static string DiscoveryTopic(IntegrationContext context, string objectId) => $"{DiscoveryPrefix(context)}/{Component}/{objectId}/config";
}
