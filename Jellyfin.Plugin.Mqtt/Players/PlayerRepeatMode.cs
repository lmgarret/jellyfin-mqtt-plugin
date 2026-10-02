namespace Jellyfin.Plugin.Mqtt.Players;

/// <summary>
/// The repeat mode of a player.
/// </summary>
public enum PlayerRepeatMode
{
    /// <summary>
    /// The queue plays once.
    /// </summary>
    Off,

    /// <summary>
    /// The queue repeats.
    /// </summary>
    All,

    /// <summary>
    /// The current item repeats.
    /// </summary>
    One
}
