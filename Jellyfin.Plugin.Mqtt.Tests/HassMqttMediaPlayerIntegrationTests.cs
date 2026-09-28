using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Mqtt.Configuration;
using Jellyfin.Plugin.Mqtt.Integrations;
using Jellyfin.Plugin.Mqtt.Integrations.HassMqttMediaPlayer;
using Jellyfin.Plugin.Mqtt.Mqtt;
using Jellyfin.Plugin.Mqtt.Players;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.Mqtt.Tests;

public class HassMqttMediaPlayerIntegrationTests
{
    private readonly HassMqttMediaPlayerIntegration _integration = new();
    private readonly IntegrationContext _context = new(
        new MqttConnection(NullLogger<MqttConnection>.Instance),
        new BridgeTopics("jellyfin", "server"),
        new PluginConfiguration());

    [Theory]
    [InlineData("jellyfin/mqtt_mediaplayer/abc/command", "abc")]
    [InlineData("jellyfin/mqtt_mediaplayer/a/b/command", null)]
    [InlineData("jellyfin/mqtt_mediaplayer//command", null)]
    [InlineData("other/mqtt_mediaplayer/abc/command", null)]
    [InlineData("jellyfin/mqtt_mediaplayer/abc/state", null)]
    [InlineData("jellyfin/players/abc/command", null)]
    public async Task HandleMessage_RecognizesCommandTopics(string topic, string? expectedKey)
    {
        var received = await _integration.HandleMessageAsync(_context, topic, "{\"pause\": true}", new HashSet<string> { "abc" }, CancellationToken.None);

        Assert.Equal(expectedKey, received?.DeviceKey);
    }

    [Fact]
    public async Task HandleMessage_ParsesJsonCommands()
    {
        var received = await _integration.HandleMessageAsync(_context, "jellyfin/mqtt_mediaplayer/abc/command", "{\"volume\": 40}", new HashSet<string> { "abc" }, CancellationToken.None);

        Assert.Equal([new PlayerCommand(PlayerCommandKind.SetVolume, 40)], received?.Commands);
    }

    [Fact]
    public async Task HandleMessage_RejectsInvalidCommands()
    {
        await Assert.ThrowsAsync<FormatException>(() => _integration.HandleMessageAsync(_context, "jellyfin/mqtt_mediaplayer/abc/command", "play", new HashSet<string> { "abc" }, CancellationToken.None));
    }

    [Fact]
    public void SerializeState_ContainsEveryKeyAndTheTopics()
    {
        using var payload = JsonDocument.Parse(HassMqttMediaPlayerIntegration.SerializeState(_context, "abc", new PlayerState
        {
            Status = PlayerStatus.Playing,
            MediaSeriesTitle = "Some Show",
            Volume = 75,
            MediaPosition = TimeSpan.FromSeconds(3),
        }));
        var root = payload.RootElement;

        Assert.Equal("playing", root.GetProperty("state").GetString());
        Assert.Equal(75, root.GetProperty("volume").GetInt32());
        Assert.Equal("Some Show", root.GetProperty("media_artist").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("media_title").ValueKind);
        Assert.Equal("jellyfin/mqtt_mediaplayer/abc/command", root.GetProperty("command_topic").GetString());
        Assert.Equal("jellyfin/mqtt_mediaplayer/abc/albumart", root.GetProperty("albumart_topic").GetString());
        Assert.False(root.TryGetProperty("media_position", out _));
    }

    [Fact]
    public void Subscriptions_UseTheBaseTopic()
    {
        Assert.Equal(["jellyfin/mqtt_mediaplayer/+/command"], _integration.GetSubscriptions(_context));
        Assert.Equal(["homeassistant/sensor/+/config", "jellyfin/mqtt_mediaplayer/+/state"], _integration.GetCleanupFilters(_context));
    }
}
