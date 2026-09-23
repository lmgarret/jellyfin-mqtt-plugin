using Jellyfin.Plugin.Mqtt.Players;
using Xunit;

namespace Jellyfin.Plugin.Mqtt.Tests;

public class PlayerTopicsTests
{
    [Fact]
    public void DeviceKey_IsStableAndTopicSafe()
    {
        var key = PlayerTopics.DeviceKey("some/device+id#");

        Assert.Equal(key, PlayerTopics.DeviceKey("some/device+id#"));
        Assert.Matches("^[0-9a-f]{12}$", key);
    }

    [Fact]
    public void ParseKey_RoundTrips()
    {
        var topics = new PlayerTopics("jellyfin/", "server");

        Assert.Equal("abc", topics.ParseKey(topics.CommandTopic("abc"), "command"));
        Assert.Equal("abc", topics.ParseKey(topics.StateTopic("abc"), "state"));
        Assert.Null(topics.ParseKey(topics.StateTopic("abc"), "command"));
        Assert.Null(topics.ParseKey("jellyfin/players/a/b/command", "command"));
        Assert.Null(topics.ParseKey("other/players/abc/command", "command"));
    }

    [Fact]
    public void BaseTopic_DefaultsWhenEmpty()
    {
        Assert.Equal("jellyfin/status", new PlayerTopics(" ", "server").StatusTopic);
    }
}
