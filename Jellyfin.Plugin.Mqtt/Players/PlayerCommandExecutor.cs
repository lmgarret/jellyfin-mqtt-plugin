using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Session;

namespace Jellyfin.Plugin.Mqtt.Players;

/// <summary>
/// Forwards player commands to a Jellyfin session. Commands run as the server.
/// </summary>
public static class PlayerCommandExecutor
{
    /// <summary>
    /// Sends a command to a session.
    /// </summary>
    /// <param name="sessionManager">The session manager.</param>
    /// <param name="sessionId">The target session id.</param>
    /// <param name="command">The command.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes once the command is sent.</returns>
    public static Task ExecuteAsync(ISessionManager sessionManager, string sessionId, PlayerCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sessionManager);
        ArgumentNullException.ThrowIfNull(command);

        return command.Kind switch
        {
            PlayerCommandKind.Play => Playstate(PlaystateCommand.Unpause),
            PlayerCommandKind.Pause => Playstate(PlaystateCommand.Pause),
            PlayerCommandKind.PlayPause => Playstate(PlaystateCommand.PlayPause),
            PlayerCommandKind.Stop => Playstate(PlaystateCommand.Stop),
            PlayerCommandKind.Next => Playstate(PlaystateCommand.NextTrack),
            PlayerCommandKind.Previous => Playstate(PlaystateCommand.PreviousTrack),
            PlayerCommandKind.Seek => sessionManager.SendPlaystateCommand(
                null,
                sessionId,
                new PlaystateRequest
                {
                    Command = PlaystateCommand.Seek,
                    SeekPositionTicks = TimeSpan.FromSeconds(Math.Max(0, command.Value)).Ticks,
                },
                cancellationToken),
            PlayerCommandKind.SetVolume => General(
                GeneralCommandType.SetVolume,
                new Dictionary<string, string>
                {
                    ["Volume"] = ((int)Math.Clamp(Math.Round(command.Value), 0, 100)).ToString(CultureInfo.InvariantCulture),
                }),
            PlayerCommandKind.SetMute => General(command.Value != 0 ? GeneralCommandType.Mute : GeneralCommandType.Unmute, null),
            _ => throw new ArgumentOutOfRangeException(nameof(command), command.Kind, "Unknown command"),
        };

        Task Playstate(PlaystateCommand playstate) =>
            sessionManager.SendPlaystateCommand(null, sessionId, new PlaystateRequest { Command = playstate }, cancellationToken);

        Task General(GeneralCommandType type, Dictionary<string, string>? arguments) =>
            sessionManager.SendGeneralCommand(null, sessionId, new GeneralCommand(arguments) { Name = type }, cancellationToken);
    }
}
