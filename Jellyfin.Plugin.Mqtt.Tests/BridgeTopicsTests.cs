using Jellyfin.Plugin.Mqtt.Players;
using Xunit;

namespace Jellyfin.Plugin.Mqtt.Tests;

public class BridgeTopicsTests
{
    [Fact]
    public void DeviceKey_IsStableAndTopicSafe()
    {
        var key = BridgeTopics.DeviceKey("some/device+id#");

        Assert.Equal(key, BridgeTopics.DeviceKey("some/device+id#"));
        Assert.Matches("^[0-9a-f]{12}$", key);
    }

    [Fact]
    public void BaseTopic_IsNormalized()
    {
        Assert.Equal("jellyfin/status", new BridgeTopics(" ", "server").StatusTopic);
        Assert.Equal("home/jf/status", new BridgeTopics("home/jf/", "server").StatusTopic);
    }
}
