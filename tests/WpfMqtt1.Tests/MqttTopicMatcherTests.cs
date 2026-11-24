using WpfMqtt1.Services;

namespace WpfMqtt1.Tests;

public sealed class MqttTopicMatcherTests
{
    [Theory]
    [InlineData("sensors/room1/temp", "sensors/room1/temp", true)]
    [InlineData("sensors/room1/temp", "sensors/room2/temp", false)]
    [InlineData("sensors/+/temp", "sensors/room1/temp", true)]
    [InlineData("sensors/+/temp", "sensors/room1/sub/temp", false)]
    [InlineData("sensors/#", "sensors/room1/temp", true)]
    [InlineData("sensors/#", "sensors", true)]
    [InlineData("#", "anything/at/all", true)]
    [InlineData("#", "$SYS/broker/uptime", false)]
    [InlineData("+/broker/uptime", "$SYS/broker/uptime", false)]
    [InlineData("$SYS/#", "$SYS/broker/uptime", true)]
    [InlineData("a/+/c/#", "a/b/c/d/e", true)]
    [InlineData("a/+/c/#", "a/b/c", true)]
    public void IsMatch_FollowsMqttWildcardSpecification(string filter, string topic, bool expected)
    {
        Assert.Equal(expected, MqttTopicMatcher.IsMatch(filter, topic));
    }
}
