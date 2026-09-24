namespace Jellyfin.Plugin.Mqtt.Players;

/// <summary>
/// The playback status of a player.
/// </summary>
public enum PlayerStatus
{
    /// <summary>
    /// No session is open on the device.
    /// </summary>
    Off,

    /// <summary>
    /// A session is open, nothing is playing.
    /// </summary>
    Idle,

    /// <summary>
    /// Media is playing.
    /// </summary>
    Playing,

    /// <summary>
    /// Media is paused.
    /// </summary>
    Paused
}
