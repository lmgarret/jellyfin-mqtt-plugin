using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Mqtt.Mqtt;
using Jellyfin.Plugin.Mqtt.Players;

namespace Jellyfin.Plugin.Mqtt.Integrations.MqttMediaPlayer;

/// <summary>
/// Exposes players to Home Assistant through the
/// <see href="https://github.com/bkbilly/mqtt_media_player">mqtt_media_player</see> custom integration.
/// </summary>
/// <remarks>
/// Topics, under the base topic:
/// <list type="bullet">
/// <item><c>mqtt_media_player/&lt;key&gt;/&lt;field&gt;</c>: one retained plain value per field (state, title, volume, …), empty when not applicable.</item>
/// <item><c>mqtt_media_player/&lt;key&gt;/set/&lt;command&gt;</c>: commands, one topic each.</item>
/// </list>
/// Players are announced on <c>homeassistant/media_player/jellyfin/jellyfin_&lt;server&gt;_&lt;key&gt;/config</c>:
/// the consumer only listens under the <c>homeassistant</c> prefix. The <c>jellyfin</c> node keeps the
/// announcements apart from those of <c>mqtt_universal_media_player</c>.
/// </remarks>
public class MqttMediaPlayerIntegration : IPlayerIntegration
{
    /// <summary>
    /// The integration id.
    /// </summary>
    public const string IntegrationId = "mqtt_media_player";

    private const string PlayersSegment = "mqtt_media_player";
    private const string SetSegment = "set";
    private const string DiscoveryRoot = "homeassistant/media_player/jellyfin";

    private const string StateField = "state";
    private const string TitleField = "title";
    private const string ArtistField = "artist";
    private const string AlbumField = "album";
    private const string DurationField = "duration";
    private const string PositionField = "position";
    private const string VolumeField = "volume";
    private const string MuteField = "mute";
    private const string AlbumArtField = "albumart";
    private const string MediaTypeField = "mediatype";

    private const string MutePayload = "mute";
    private const string UnmutePayload = "unmute";

    private static readonly string[] _fields =
        [StateField, TitleField, ArtistField, AlbumField, DurationField, PositionField, VolumeField, MuteField, AlbumArtField, MediaTypeField];

    private static readonly Dictionary<string, PlayerCommandKind> _triggers = new(StringComparer.Ordinal)
    {
        ["play"] = PlayerCommandKind.Play,
        ["pause"] = PlayerCommandKind.Pause,
        ["playpause"] = PlayerCommandKind.PlayPause,
        ["next"] = PlayerCommandKind.Next,
        ["previous"] = PlayerCommandKind.Previous,
    };

    // Last published values per device key, so unchanged fields are not sent again:
    // the consumer writes the entity state on every message it receives.
    private readonly ConcurrentDictionary<string, Dictionary<string, string>> _published = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public string Id => IntegrationId;

    /// <inheritdoc />
    public string Name => "Home Assistant: MQTT Media Player";

    /// <inheritdoc />
    public string Description => "Requires the mqtt_media_player custom integration, which only discovers players under the homeassistant prefix. Stop and seek are not supported.";

    /// <inheritdoc />
    public Uri RepositoryUrl { get; } = new("https://github.com/bkbilly/mqtt_media_player");

    /// <inheritdoc />
    public IReadOnlyList<IntegrationSetting> Settings { get; } = [];

    /// <inheritdoc />
    public IEnumerable<string> GetSubscriptions(IntegrationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return [PlayerTopic(context, "+", $"{SetSegment}/+")];
    }

    /// <inheritdoc />
    public IEnumerable<string> GetCleanupFilters(IntegrationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return [$"{DiscoveryRoot}/+/config", PlayerTopic(context, "+", StateField)];
    }

    /// <inheritdoc />
    public Task PublishDeviceAsync(IntegrationContext context, ExposedDevice device, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(device);

        // Announcing happens on every (re)connect, before the first state: send every field again.
        _published.TryRemove(device.Key, out _);

        var objectId = ObjectId(context, device.Key);
        var payload = new Dictionary<string, object?>
        {
            ["name"] = device.Name,
            ["device"] = new Dictionary<string, object?>
            {
                ["identifiers"] = new[] { objectId },
                ["name"] = device.Name,
                ["manufacturer"] = "Jellyfin",
                ["model"] = device.AppName,
                ["sw_version"] = device.AppVersion,
            },
            ["availability"] = new Dictionary<string, object?>
            {
                ["topic"] = context.Topics.StatusTopic,
                ["payload_available"] = MqttConnection.OnlinePayload,
                ["payload_not_available"] = MqttConnection.OfflinePayload,
            },
        };

        foreach (var field in _fields)
        {
            payload[$"state_{field}_topic"] = PlayerTopic(context, device.Key, field);
        }

        foreach (var command in _triggers.Keys)
        {
            payload[$"command_{command}_topic"] = CommandTopic(context, device.Key, command);
        }

        payload["command_volume_topic"] = CommandTopic(context, device.Key, VolumeField);
        payload["command_mute_topic"] = CommandTopic(context, device.Key, MuteField);

        return context.Connection.PublishAsync(DiscoveryTopic(objectId), JsonSerializer.Serialize(payload), true, cancellationToken);
    }

    /// <inheritdoc />
    public async Task PublishStateAsync(IntegrationContext context, PlayerUpdate update, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(update);

        var key = update.Device.Key;
        var published = _published.GetOrAdd(key, _ => new Dictionary<string, string>(StringComparer.Ordinal));

        // The artwork goes out before the metadata, so the card never shows a new title with old artwork.
        if (update.ImageChanged)
        {
            var image = await update.GetImageAsync(cancellationToken).ConfigureAwait(false);
            await context.Connection.PublishAsync(PlayerTopic(context, key, AlbumArtField), image is null ? string.Empty : Convert.ToBase64String(image), true, cancellationToken).ConfigureAwait(false);
        }

        foreach (var (field, value) in SerializeState(update.State))
        {
            // The consumer stamps the position when it receives it, so it is always sent.
            if (field != PositionField && published.TryGetValue(field, out var previous) && previous == value)
            {
                continue;
            }

            await context.Connection.PublishAsync(PlayerTopic(context, key, field), value, true, cancellationToken).ConfigureAwait(false);
            published[field] = value;
        }
    }

    /// <inheritdoc />
    public async Task RemoveDeviceAsync(IntegrationContext context, string deviceKey, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        await context.Connection.PublishAsync(DiscoveryTopic(ObjectId(context, deviceKey)), string.Empty, true, cancellationToken).ConfigureAwait(false);
        await ClearPlayerAsync(context, deviceKey, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<PlayerCommands?> HandleMessageAsync(IntegrationContext context, string topic, string payload, IReadOnlySet<string> exposedKeys, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(topic);
        ArgumentNullException.ThrowIfNull(exposedKeys);

        if (ParseCommandTopic(context, topic) is (var commandKey, var command))
        {
            return new PlayerCommands(commandKey, [ParseCommand(command, payload)]);
        }

        // Cleanup of retained messages left by players that are no longer exposed.
        if (string.IsNullOrEmpty(payload))
        {
            return null;
        }

        if (ParsePlayerKey(context, topic, StateField) is { } stateKey)
        {
            if (!exposedKeys.Contains(stateKey))
            {
                await ClearPlayerAsync(context, stateKey, cancellationToken).ConfigureAwait(false);
            }

            return null;
        }

        var discoveryPrefix = DiscoveryRoot + "/";
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
    /// Serializes a state into the plain value of each field, empty when not applicable.
    /// The artwork is not included, it is published separately as it changes.
    /// </summary>
    /// <param name="state">The state.</param>
    /// <returns>The value of each field, by field name.</returns>
    public static IReadOnlyList<KeyValuePair<string, string>> SerializeState(PlayerState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        return
        [
            new(StateField, state.Status.ToString().ToLowerInvariant()),
            new(TitleField, state.MediaTitle ?? string.Empty),

            // The consumer has no series field: show it in place of the artist, as Home Assistant cards do.
            new(ArtistField, state.MediaArtist ?? state.MediaSeriesTitle ?? string.Empty),
            new(AlbumField, state.MediaAlbumName ?? string.Empty),
            new(DurationField, Seconds(state.MediaDuration)),
            new(PositionField, Seconds(state.MediaPosition)),
            new(VolumeField, state.Volume is { } volume ? (volume / 100.0).ToString("0.##", CultureInfo.InvariantCulture) : string.Empty),
            new(MuteField, state.Muted switch { true => MutePayload, false => UnmutePayload, null => string.Empty }),
            new(MediaTypeField, state.MediaContentType ?? string.Empty),
        ];
    }

    /// <summary>
    /// Parses the payload of a command topic.
    /// </summary>
    /// <param name="command">The last segment of the command topic.</param>
    /// <param name="payload">The payload.</param>
    /// <returns>The command.</returns>
    /// <exception cref="FormatException">The command is unknown or its payload is invalid.</exception>
    public static PlayerCommand ParseCommand(string command, string payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        if (_triggers.TryGetValue(command, out var kind))
        {
            return new PlayerCommand(kind);
        }

        switch (command)
        {
            case VolumeField:
                if (double.TryParse(payload, NumberStyles.Float, CultureInfo.InvariantCulture, out var volume))
                {
                    return new PlayerCommand(PlayerCommandKind.SetVolume, Math.Clamp(volume, 0, 1) * 100);
                }

                throw new FormatException($"Command '{command}' expects a number from 0 to 1");
            case MuteField:
                return payload.Trim().ToLowerInvariant() switch
                {
                    MutePayload => new PlayerCommand(PlayerCommandKind.SetMute, 1),
                    UnmutePayload => new PlayerCommand(PlayerCommandKind.SetMute, 0),
                    _ => throw new FormatException($"Command '{command}' expects '{MutePayload}' or '{UnmutePayload}'"),
                };
            default:
                throw new FormatException($"Unknown command '{command}'");
        }
    }

    private static string Seconds(TimeSpan? value) =>
        value is null ? string.Empty : ((long)value.Value.TotalSeconds).ToString(CultureInfo.InvariantCulture);

    private static string PlayerTopic(IntegrationContext context, string key, string suffix) =>
        $"{context.Topics.BaseTopic}/{PlayersSegment}/{key}/{suffix}";

    private static string CommandTopic(IntegrationContext context, string key, string command) =>
        PlayerTopic(context, key, $"{SetSegment}/{command}");

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

    private static (string Key, string Command)? ParseCommandTopic(IntegrationContext context, string topic)
    {
        var separator = topic.LastIndexOf('/');
        if (separator < 0 || separator == topic.Length - 1)
        {
            return null;
        }

        var command = topic[(separator + 1)..];
        return ParsePlayerKey(context, topic[..separator], SetSegment) is { } key ? (key, command) : null;
    }

    private static string ObjectId(IntegrationContext context, string deviceKey) => $"jellyfin_{context.Topics.ServerKey}_{deviceKey}";

    private static string DiscoveryTopic(string objectId) => $"{DiscoveryRoot}/{objectId}/config";

    private async Task ClearPlayerAsync(IntegrationContext context, string key, CancellationToken cancellationToken)
    {
        _published.TryRemove(key, out _);
        foreach (var field in _fields)
        {
            await context.Connection.PublishAsync(PlayerTopic(context, key, field), string.Empty, true, cancellationToken).ConfigureAwait(false);
        }
    }
}
