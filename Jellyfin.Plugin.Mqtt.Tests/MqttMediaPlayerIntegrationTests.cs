using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Mqtt.Configuration;
using Jellyfin.Plugin.Mqtt.Integrations;
using Jellyfin.Plugin.Mqtt.Integrations.MqttMediaPlayer;
using Jellyfin.Plugin.Mqtt.Mqtt;
using Jellyfin.Plugin.Mqtt.Players;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.Mqtt.Tests;

public class MqttMediaPlayerIntegrationTests
{
    private readonly MqttMediaPlayerIntegration _integration = new();
    private readonly IntegrationContext _context = new(
        new MqttConnection(NullLogger<MqttConnection>.Instance),
        new BridgeTopics("jellyfin", "server"),
        new PluginConfiguration());

    [Theory]
    [InlineData("play", "Play", PlayerCommandKind.Play, 0)]
    [InlineData("pause", "Pause", PlayerCommandKind.Pause, 0)]
    [InlineData("playpause", "PlayPause", PlayerCommandKind.PlayPause, 0)]
    [InlineData("next", "Next", PlayerCommandKind.Next, 0)]
    [InlineData("previous", "", PlayerCommandKind.Previous, 0)]
    [InlineData("volume", "0.4", PlayerCommandKind.SetVolume, 40)]
    [InlineData("volume", "1.5", PlayerCommandKind.SetVolume, 100)]
    [InlineData("mute", "mute", PlayerCommandKind.SetMute, 1)]
    [InlineData("mute", "unmute", PlayerCommandKind.SetMute, 0)]
    public void ParseCommand_MapsEveryCommand(string command, string payload, PlayerCommandKind kind, double value)
    {
        Assert.Equal(new PlayerCommand(kind, value), MqttMediaPlayerIntegration.ParseCommand(command, payload));
    }

    [Theory]
    [InlineData("dance", "")]
    [InlineData("volume", "loud")]
    [InlineData("mute", "true")]
    public void ParseCommand_RejectsInvalidPayloads(string command, string payload)
    {
        Assert.Throws<FormatException>(() => MqttMediaPlayerIntegration.ParseCommand(command, payload));
    }

    [Theory]
    [InlineData("jellyfin/mqtt_media_player/abc/set/pause", "abc")]
    [InlineData("jellyfin/mqtt_media_player/a/b/set/pause", null)]
    [InlineData("jellyfin/mqtt_media_player//set/pause", null)]
    [InlineData("other/mqtt_media_player/abc/set/pause", null)]
    [InlineData("jellyfin/mqtt_media_player/abc/state", null)]
    [InlineData("jellyfin/players/abc/command", null)]
    public async Task HandleMessage_RecognizesCommandTopics(string topic, string? expectedKey)
    {
        var received = await _integration.HandleMessageAsync(_context, topic, "Pause", new HashSet<string> { "abc" }, CancellationToken.None);

        Assert.Equal(expectedKey, received?.DeviceKey);
    }

    [Fact]
    public void SerializeState_WritesPlainValues()
    {
        var values = MqttMediaPlayerIntegration.SerializeState(new PlayerState
        {
            Status = PlayerStatus.Playing,
            MediaTitle = "Pilot",
            MediaSeriesTitle = "Some Show",
            MediaContentType = "episode",
            Volume = 75,
            Muted = false,
            MediaPosition = TimeSpan.FromSeconds(3.7),
        }).ToDictionary(p => p.Key, p => p.Value);

        Assert.Equal("playing", values["state"]);
        Assert.Equal("Pilot", values["title"]);
        Assert.Equal("Some Show", values["artist"]);
        Assert.Equal(string.Empty, values["album"]);
        Assert.Equal(string.Empty, values["duration"]);
        Assert.Equal("3", values["position"]);
        Assert.Equal("0.75", values["volume"]);
        Assert.Equal("unmute", values["mute"]);
        Assert.Equal("episode", values["mediatype"]);
        Assert.False(values.ContainsKey("albumart"));
    }

    [Fact]
    public void Subscriptions_UseTheBaseTopic()
    {
        Assert.Equal(["jellyfin/mqtt_media_player/+/set/+"], _integration.GetSubscriptions(_context));
        Assert.Equal(["homeassistant/media_player/jellyfin/+/config", "jellyfin/mqtt_media_player/+/state"], _integration.GetCleanupFilters(_context));
    }
}
