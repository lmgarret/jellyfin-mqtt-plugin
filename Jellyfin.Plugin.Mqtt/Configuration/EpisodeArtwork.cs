namespace Jellyfin.Plugin.Mqtt.Configuration;

/// <summary>
/// The artwork published while an episode plays. The next levels are used when the preferred one has no image.
/// </summary>
public enum EpisodeArtwork
{
    /// <summary>
    /// The episode image.
    /// </summary>
    Episode,

    /// <summary>
    /// The season image.
    /// </summary>
    Season,

    /// <summary>
    /// The series image.
    /// </summary>
    Series,
}
