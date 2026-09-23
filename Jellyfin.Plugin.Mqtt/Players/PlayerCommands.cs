using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Session;

namespace Jellyfin.Plugin.Mqtt.Players;

/// <summary>
/// Parses the JSON commands received on a player command topic, e.g. <c>{"pause": true}</c>
/// or <c>{"volume": 40}</c>, and forwards them to the Jellyfin session. Commands run as the server.
/// </summary>
public static class PlayerCommands
{
    /// <summary>Resumes playback.</summary>
    public const string Play = "play";

    /// <summary>Pauses playback.</summary>
    public const string Pause = "pause";

    /// <summary>Toggles between play and pause.</summary>
    public const string PlayPause = "play_pause";

    /// <summary>Stops playback.</summary>
    public const string Stop = "stop";

    /// <summary>Skips to the next item.</summary>
    public const string Next = "next";

    /// <summary>Goes back to the previous item.</summary>
    public const string Previous = "previous";

    /// <summary>Seeks to a position, in seconds.</summary>
    public const string Seek = "seek";

    /// <summary>Sets the volume, from 0 to 100.</summary>
    public const string Volume = "volume";

    /// <summary>Mutes (true) or unmutes (false).</summary>
    public const string Mute = "mute";

    private static readonly Dictionary<string, PlaystateCommand> _playstateCommands = new(StringComparer.Ordinal)
    {
        [Play] = PlaystateCommand.Unpause,
        [Pause] = PlaystateCommand.Pause,
        [PlayPause] = PlaystateCommand.PlayPause,
        [Stop] = PlaystateCommand.Stop,
        [Next] = PlaystateCommand.NextTrack,
        [Previous] = PlaystateCommand.PreviousTrack,
    };

    /// <summary>
    /// Executes every command of a payload against a session.
    /// </summary>
    /// <param name="sessionManager">The session manager.</param>
    /// <param name="sessionId">The target session id.</param>
    /// <param name="payload">The JSON payload.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes once every command is sent.</returns>
    /// <exception cref="FormatException">The payload is not a valid command.</exception>
    public static async Task ExecuteAsync(ISessionManager sessionManager, string sessionId, string payload, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sessionManager);

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

            foreach (var property in document.RootElement.EnumerateObject())
            {
                await ExecuteAsync(sessionManager, sessionId, property.Name, property.Value, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static Task ExecuteAsync(ISessionManager sessionManager, string sessionId, string name, JsonElement value, CancellationToken cancellationToken)
    {
        if (_playstateCommands.TryGetValue(name, out var playstate))
        {
            return sessionManager.SendPlaystateCommand(null, sessionId, new PlaystateRequest { Command = playstate }, cancellationToken);
        }

        switch (name)
        {
            case Seek:
                var seconds = ReadNumber(name, value);
                var request = new PlaystateRequest
                {
                    Command = PlaystateCommand.Seek,
                    SeekPositionTicks = TimeSpan.FromSeconds(Math.Max(0, seconds)).Ticks,
                };
                return sessionManager.SendPlaystateCommand(null, sessionId, request, cancellationToken);

            case Volume:
                var volume = (int)Math.Clamp(Math.Round(ReadNumber(name, value)), 0, 100);
                var setVolume = new GeneralCommand(new Dictionary<string, string>
                {
                    ["Volume"] = volume.ToString(CultureInfo.InvariantCulture),
                })
                {
                    Name = GeneralCommandType.SetVolume,
                };
                return sessionManager.SendGeneralCommand(null, sessionId, setVolume, cancellationToken);

            case Mute:
                if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                {
                    throw new FormatException($"Command '{name}' expects a boolean");
                }

                var mute = new GeneralCommand
                {
                    Name = value.GetBoolean() ? GeneralCommandType.Mute : GeneralCommandType.Unmute,
                };
                return sessionManager.SendGeneralCommand(null, sessionId, mute, cancellationToken);

            default:
                throw new FormatException($"Unknown command '{name}'");
        }
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
}
