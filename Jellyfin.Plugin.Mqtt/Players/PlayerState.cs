using System;

namespace Jellyfin.Plugin.Mqtt.Players;

/// <summary>
/// The state of a player, independent of any wire format.
/// </summary>
public sealed record PlayerState
{
    /// <summary>
    /// Gets the playback status.
    /// </summary>
    public PlayerStatus Status { get; init; }

    /// <summary>
    /// Gets the device display name.
    /// </summary>
    public string? DeviceName { get; init; }

    /// <summary>
    /// Gets the client application name.
    /// </summary>
    public string? AppName { get; init; }

    /// <summary>
    /// Gets the name of the user of the current session.
    /// </summary>
    public string? UserName { get; init; }

    /// <summary>
    /// Gets the volume, from 0 to 100.
    /// </summary>
    public int? Volume { get; init; }

    /// <summary>
    /// Gets whether the player is muted.
    /// </summary>
    public bool? Muted { get; init; }

    /// <summary>
    /// Gets the id of the playing item.
    /// </summary>
    public string? MediaId { get; init; }

    /// <summary>
    /// Gets the title of the playing item.
    /// </summary>
    public string? MediaTitle { get; init; }

    /// <summary>
    /// Gets the artists of the playing item.
    /// </summary>
    public string? MediaArtist { get; init; }

    /// <summary>
    /// Gets the album of the playing item.
    /// </summary>
    public string? MediaAlbumName { get; init; }

    /// <summary>
    /// Gets the series of the playing item.
    /// </summary>
    public string? MediaSeriesTitle { get; init; }

    /// <summary>
    /// Gets the season number of the playing item.
    /// </summary>
    public int? MediaSeason { get; init; }

    /// <summary>
    /// Gets the episode number of the playing item.
    /// </summary>
    public int? MediaEpisode { get; init; }

    /// <summary>
    /// Gets the content type of the playing item: music, episode, movie, channel, image or video.
    /// </summary>
    public string? MediaContentType { get; init; }

    /// <summary>
    /// Gets the artwork of the playing item.
    /// </summary>
    public ImageReference? MediaImage { get; init; }

    /// <summary>
    /// Gets the artwork URL of the playing item.
    /// </summary>
    public string? MediaImageUrl { get; init; }

    /// <summary>
    /// Gets the playback position.
    /// </summary>
    public TimeSpan? MediaPosition { get; init; }

    /// <summary>
    /// Gets the duration of the playing item.
    /// </summary>
    public TimeSpan? MediaDuration { get; init; }
}
