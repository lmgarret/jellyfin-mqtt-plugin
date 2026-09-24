using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Mqtt.Configuration;
using Jellyfin.Plugin.Mqtt.Integrations;
using Jellyfin.Plugin.Mqtt.Integrations.UniversalMediaPlayer;
using Jellyfin.Plugin.Mqtt.Mqtt;
using Jellyfin.Plugin.Mqtt.Players;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.Mqtt.Tests;

public class UniversalMediaPlayerIntegrationTests
{
    private readonly UniversalMediaPlayerIntegration _integration = new();
    private readonly IntegrationContext _context = new(
        new MqttConnection(NullLogger<MqttConnection>.Instance),
        new BridgeTopics("jellyfin", "server"),
        new PluginConfiguration());

    [Fact]
    public void ParseCommands_MapsEveryKey()
    {
        var commands = UniversalMediaPlayerIntegration.ParseCommands(
            "{\"play\": true, \"pause\": true, \"play_pause\": true, \"stop\": true, \"next\": true, \"previous\": true, \"seek\": \"12.5\", \"volume\": 40, \"mute\": true}");

        Assert.Equal(
            [
                new PlayerCommand(PlayerCommandKind.Play),
                new PlayerCommand(PlayerCommandKind.Pause),
                new PlayerCommand(PlayerCommandKind.PlayPause),
                new PlayerCommand(PlayerCommandKind.Stop),
                new PlayerCommand(PlayerCommandKind.Next),
                new PlayerCommand(PlayerCommandKind.Previous),
                new PlayerCommand(PlayerCommandKind.Seek, 12.5),
                new PlayerCommand(PlayerCommandKind.SetVolume, 40),
                new PlayerCommand(PlayerCommandKind.SetMute, 1),
            ],
            commands);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[1]")]
    [InlineData("{\"dance\": true}")]
    [InlineData("{\"volume\": \"loud\"}")]
    [InlineData("{\"mute\": 1}")]
    public void ParseCommands_RejectsInvalidPayloads(string payload)
    {
        Assert.Throws<FormatException>(() => UniversalMediaPlayerIntegration.ParseCommands(payload));
    }

    [Theory]
    [InlineData("jellyfin/players/abc/command", "abc")]
    [InlineData("jellyfin/players/a/b/command", null)]
    [InlineData("jellyfin/players//command", null)]
    [InlineData("other/players/abc/command", null)]
    [InlineData("jellyfin/players/abc/state", null)]
    public async Task HandleMessage_RecognizesCommandTopics(string topic, string? expectedKey)
    {
        var received = await _integration.HandleMessageAsync(_context, topic, "{\"stop\": true}", new HashSet<string> { "abc" }, CancellationToken.None);

        Assert.Equal(expectedKey, received?.DeviceKey);
    }

    [Fact]
    public void SerializeState_ContainsEveryKey()
    {
        using var payload = JsonDocument.Parse(UniversalMediaPlayerIntegration.SerializeState(new PlayerState { MediaPosition = TimeSpan.FromSeconds(3) }));
        var root = payload.RootElement;

        Assert.Equal("off", root.GetProperty("state").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("media_title").ValueKind);
        Assert.Equal(3, root.GetProperty("media_position").GetInt64());
        Assert.False(root.TryGetProperty("media_image", out _));
    }

    [Fact]
    public void Subscriptions_UseTheBaseTopic()
    {
        Assert.Equal(["jellyfin/players/+/command"], _integration.GetSubscriptions(_context));
        Assert.Equal(["homeassistant/media_player/+/config", "jellyfin/players/+/state"], _integration.GetCleanupFilters(_context));
    }
}
