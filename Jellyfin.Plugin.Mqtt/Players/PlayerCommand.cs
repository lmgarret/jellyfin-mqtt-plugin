namespace Jellyfin.Plugin.Mqtt.Players;

/// <summary>
/// The kind of a player command.
/// </summary>
public enum PlayerCommandKind
{
    /// <summary>Resumes playback.</summary>
    Play,

    /// <summary>Pauses playback.</summary>
    Pause,

    /// <summary>Toggles between play and pause.</summary>
    PlayPause,

    /// <summary>Stops playback.</summary>
    Stop,

    /// <summary>Skips to the next item.</summary>
    Next,

    /// <summary>Goes back to the previous item.</summary>
    Previous,

    /// <summary>Seeks to <see cref="PlayerCommand.Value"/> seconds.</summary>
    Seek,

    /// <summary>Sets the volume to <see cref="PlayerCommand.Value"/>, from 0 to 100.</summary>
    SetVolume,

    /// <summary>Mutes when <see cref="PlayerCommand.Value"/> is non-zero, unmutes otherwise.</summary>
    SetMute
}

/// <summary>
/// A command for a player, independent of any wire format.
/// </summary>
/// <param name="Kind">The command kind.</param>
/// <param name="Value">The argument of <see cref="PlayerCommandKind.Seek"/>, <see cref="PlayerCommandKind.SetVolume"/> and <see cref="PlayerCommandKind.SetMute"/>.</param>
public sealed record PlayerCommand(PlayerCommandKind Kind, double Value = 0);
