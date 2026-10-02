using System;
using System.Globalization;
using System.Linq;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.Mqtt.Configuration;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Session;

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
    /// <param name="serverUrl">The server URL used for artwork links, empty to omit the link.</param>
    /// <param name="episodeArtwork">The artwork published while an episode plays.</param>
    /// <param name="primaryImageTag">Gets the primary image tag of a library item, null when it has none.</param>
    /// <returns>The player state.</returns>
    public static PlayerState Build(
        ExposedDevice device,
        SessionInfo? session,
        string serverUrl,
        EpisodeArtwork episodeArtwork,
        Func<Guid, string?> primaryImageTag)
    {
        ArgumentNullException.ThrowIfNull(primaryImageTag);

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
        var supported = session.SupportedCommands ?? [];
        state = state with
        {
            Status = PlayerStatus.Idle,
            UserName = session.UserName,
            Volume = playState?.VolumeLevel,
            Muted = playState?.IsMuted,
            VolumeSupported = supported.Contains(GeneralCommandType.SetVolume),
            Shuffle = supported.Contains(GeneralCommandType.SetShuffleQueue) ? playState?.PlaybackOrder == PlaybackOrder.Shuffle : null,
            Repeat = supported.Contains(GeneralCommandType.SetRepeatMode) ? Repeat(playState?.RepeatMode) : null,
        };

        var item = session.NowPlayingItem;
        if (item is null)
        {
            return state;
        }

        var image = Image(item, episodeArtwork, primaryImageTag);
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
            MediaImage = image,
            MediaImageUrl = ImageUrl(image, serverUrl),
            MediaPosition = playState?.PositionTicks is long position ? TimeSpan.FromTicks(position) : null,
            MediaDuration = item.RunTimeTicks is long runtime ? TimeSpan.FromTicks(runtime) : null,
        };
    }

    private static PlayerRepeatMode Repeat(RepeatMode? mode) => mode switch
    {
        RepeatMode.RepeatAll => PlayerRepeatMode.All,
        RepeatMode.RepeatOne => PlayerRepeatMode.One,
        _ => PlayerRepeatMode.Off,
    };

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

    private static ImageReference? Image(BaseItemDto item, EpisodeArtwork episodeArtwork, Func<Guid, string?> primaryImageTag)
    {
        var own = item.ImageTags?.TryGetValue(ImageType.Primary, out var primaryTag) == true ? Reference(item.Id, primaryTag) : null;
        var parent = Reference(item.ParentPrimaryImageItemId, item.ParentPrimaryImageTag);
        if (item.Type != BaseItemKind.Episode)
        {
            return own ?? Reference(item.AlbumId, item.AlbumPrimaryImageTag) ?? parent;
        }

        // The DTO carries no season image tag, so it is looked up only when needed.
        ImageReference? Season() => item.SeasonId is Guid seasonId ? Reference(seasonId, primaryImageTag(seasonId)) : null;
        var series = Reference(item.SeriesId, item.SeriesPrimaryImageTag);
        var preferred = episodeArtwork switch
        {
            EpisodeArtwork.Season => Season() ?? series ?? own,
            EpisodeArtwork.Series => series ?? Season() ?? own,
            _ => own ?? Season() ?? series,
        };

        return preferred ?? parent;
    }

    private static ImageReference? Reference(Guid? id, string? tag) => id is null || tag is null ? null : new ImageReference(id.Value, tag);

    private static string? ImageUrl(ImageReference? image, string serverUrl)
    {
        if (image is null || string.IsNullOrWhiteSpace(serverUrl))
        {
            return null;
        }

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{serverUrl.TrimEnd('/')}/Items/{image.ItemId:N}/Images/Primary?tag={Uri.EscapeDataString(image.Tag)}&maxWidth=600");
    }

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
