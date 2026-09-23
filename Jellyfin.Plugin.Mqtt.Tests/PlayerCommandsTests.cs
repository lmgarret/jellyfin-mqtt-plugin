using System;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Mqtt.Players;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Session;
using NSubstitute;
using Xunit;

namespace Jellyfin.Plugin.Mqtt.Tests;

public class PlayerCommandsTests
{
    private readonly ISessionManager _sessionManager = Substitute.For<ISessionManager>();

    [Theory]
    [InlineData("play", PlaystateCommand.Unpause)]
    [InlineData("pause", PlaystateCommand.Pause)]
    [InlineData("play_pause", PlaystateCommand.PlayPause)]
    [InlineData("stop", PlaystateCommand.Stop)]
    [InlineData("next", PlaystateCommand.NextTrack)]
    [InlineData("previous", PlaystateCommand.PreviousTrack)]
    public async Task PlaystateCommands_AreForwardedAsServer(string key, PlaystateCommand expected)
    {
        await PlayerCommands.ExecuteAsync(_sessionManager, "session", $"{{\"{key}\": true}}", CancellationToken.None);

        await _sessionManager.Received(1).SendPlaystateCommand(null, "session", Arg.Is<PlaystateRequest>(r => r.Command == expected), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Seek_ConvertsSecondsToTicks()
    {
        await PlayerCommands.ExecuteAsync(_sessionManager, "session", "{\"seek\": 12.5}", CancellationToken.None);

        await _sessionManager.Received(1).SendPlaystateCommand(
            null,
            "session",
            Arg.Is<PlaystateRequest>(r => r.Command == PlaystateCommand.Seek && r.SeekPositionTicks == TimeSpan.FromSeconds(12.5).Ticks),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task VolumeAndMute_AreGeneralCommands()
    {
        await PlayerCommands.ExecuteAsync(_sessionManager, "session", "{\"volume\": 140, \"mute\": false}", CancellationToken.None);

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

    [Theory]
    [InlineData("not json")]
    [InlineData("[1]")]
    [InlineData("{\"dance\": true}")]
    [InlineData("{\"volume\": \"loud\"}")]
    [InlineData("{\"mute\": 1}")]
    public async Task InvalidCommands_Throw(string payload)
    {
        await Assert.ThrowsAsync<FormatException>(() => PlayerCommands.ExecuteAsync(_sessionManager, "session", payload, CancellationToken.None));
    }
}
