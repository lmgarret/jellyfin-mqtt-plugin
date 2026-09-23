using System;
using System.Globalization;
using System.Linq;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.Mqtt.Players;

/// <summary>
/// Builds player states from Jellyfin sessions.
/// </summary>
public static class PlayerStateBuilder
{
    /// <summary>
    /// Builds the state of a device.
    /// </summary>
    /// <param name="device">The exposed device.</param>
    /// <param name="session">The session currently open on the device, if any.</param>
    /// <param name="serverUrl">The server URL used for artwork links, empty to omit artwork.</param>
    /// <returns>The player state.</returns>
    public static PlayerState Build(ExposedDevice device, SessionInfo? session, string serverUrl)
    {
        var state = new PlayerState
        {
            DeviceName = device.Name,
            AppName = session?.Client ?? device.AppName,
        };

        if (session is null)
        {
            return state;
        }

        var playState = session.PlayState;
        state = state with
        {
            Status = PlayerStatus.Idle,
            UserName = session.UserName,
            Volume = playState?.VolumeLevel,
            Muted = playState?.IsMuted,
        };

        var item = session.NowPlayingItem;
        if (item is null)
        {
            return state;
        }

        return state with
        {
            Status = playState?.IsPaused == true ? PlayerStatus.Paused : PlayerStatus.Playing,
            MediaId = item.Id.ToString("N", CultureInfo.InvariantCulture),
            MediaTitle = item.Name,
            MediaArtist = Artist(item),
            MediaAlbumName = NullIfEmpty(item.Album),
            MediaSeriesTitle = NullIfEmpty(item.SeriesName),
            MediaSeason = item.Type == BaseItemKind.Episode ? item.ParentIndexNumber : null,
            MediaEpisode = item.Type == BaseItemKind.Episode ? item.IndexNumber : null,
            MediaContentType = ContentType(item),
            MediaImageUrl = ImageUrl(item, serverUrl),
            MediaPosition = playState?.PositionTicks is long position ? TimeSpan.FromTicks(position) : null,
            MediaDuration = item.RunTimeTicks is long runtime ? TimeSpan.FromTicks(runtime) : null,
        };
    }

    private static string? Artist(BaseItemDto item)
    {
        if (item.Artists is { Count: > 0 } artists)
        {
            return string.Join(", ", artists);
        }

        return NullIfEmpty(item.AlbumArtist);
    }

    private static string? ContentType(BaseItemDto item) => item.Type switch
    {
        BaseItemKind.Audio or BaseItemKind.AudioBook => "music",
        BaseItemKind.Episode => "episode",
        BaseItemKind.Movie => "movie",
        BaseItemKind.TvChannel or BaseItemKind.LiveTvChannel => "channel",
        BaseItemKind.Photo => "image",
        _ => item.MediaType == MediaType.Audio ? "music" : "video",
    };

    private static string? ImageUrl(BaseItemDto item, string serverUrl)
    {
        if (string.IsNullOrWhiteSpace(serverUrl))
        {
            return null;
        }

        (Guid? id, string? tag) = item.ImageTags?.TryGetValue(ImageType.Primary, out var primaryTag) == true
            ? (item.Id, primaryTag)
            : item.Type switch
            {
                BaseItemKind.Episode when item.SeriesPrimaryImageTag is not null => (item.SeriesId, item.SeriesPrimaryImageTag),
                _ when item.AlbumPrimaryImageTag is not null => (item.AlbumId, item.AlbumPrimaryImageTag),
                _ when item.ParentPrimaryImageTag is not null => (item.ParentPrimaryImageItemId, item.ParentPrimaryImageTag),
                _ => ((Guid?)null, (string?)null),
            };

        if (id is null || tag is null)
        {
            return null;
        }

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{serverUrl.TrimEnd('/')}/Items/{id.Value:N}/Images/Primary?tag={Uri.EscapeDataString(tag)}&maxWidth=600");
    }

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
