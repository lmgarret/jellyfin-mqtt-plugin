using System;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Mqtt.Players;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Session;
using NSubstitute;
using Xunit;

namespace Jellyfin.Plugin.Mqtt.Tests;

public class PlayerCommandExecutorTests
{
    private readonly ISessionManager _sessionManager = Substitute.For<ISessionManager>();

    [Theory]
    [InlineData(PlayerCommandKind.Play, PlaystateCommand.Unpause)]
    [InlineData(PlayerCommandKind.Pause, PlaystateCommand.Pause)]
    [InlineData(PlayerCommandKind.PlayPause, PlaystateCommand.PlayPause)]
    [InlineData(PlayerCommandKind.Stop, PlaystateCommand.Stop)]
    [InlineData(PlayerCommandKind.Next, PlaystateCommand.NextTrack)]
    [InlineData(PlayerCommandKind.Previous, PlaystateCommand.PreviousTrack)]
    public async Task PlaystateCommands_AreForwardedAsServer(PlayerCommandKind kind, PlaystateCommand expected)
    {
        await PlayerCommandExecutor.ExecuteAsync(_sessionManager, "session", new PlayerCommand(kind), CancellationToken.None);

        await _sessionManager.Received(1).SendPlaystateCommand(null, "session", Arg.Is<PlaystateRequest>(r => r.Command == expected), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Seek_ConvertsSecondsToTicks()
    {
        await PlayerCommandExecutor.ExecuteAsync(_sessionManager, "session", new PlayerCommand(PlayerCommandKind.Seek, 12.5), CancellationToken.None);

        await _sessionManager.Received(1).SendPlaystateCommand(
            null,
            "session",
            Arg.Is<PlaystateRequest>(r => r.Command == PlaystateCommand.Seek && r.SeekPositionTicks == TimeSpan.FromSeconds(12.5).Ticks),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task VolumeAndMute_AreGeneralCommands()
    {
        await PlayerCommandExecutor.ExecuteAsync(_sessionManager, "session", new PlayerCommand(PlayerCommandKind.SetVolume, 140), CancellationToken.None);
        await PlayerCommandExecutor.ExecuteAsync(_sessionManager, "session", new PlayerCommand(PlayerCommandKind.SetMute, 0), CancellationToken.None);

        await _sessionManager.Received(1).SendGeneralCommand(
            null,
            "session",
            Arg.Is<GeneralCommand>(c => c.Name == GeneralCommandType.SetVolume && c.Arguments["Volume"] == "100"),
            Arg.Any<CancellationToken>());
        await _sessionManager.Received(1).SendGeneralCommand(
            null,
            "session",
            Arg.Is<GeneralCommand>(c => c.Name == GeneralCommandType.Unmute),
            Arg.Any<CancellationToken>());
    }
}
