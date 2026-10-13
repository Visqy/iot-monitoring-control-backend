using IotBackend.BackgroundServices;
using Xunit;

namespace IotBackend.Tests.Unit;

public class MqttTopicTests
{
    [Theory]
    [InlineData("terminal1/pzem", "terminal1")]
    [InlineData("terminal1/relay/state", "terminal1")]
    [InlineData("a/b/c/d", "a")]
    public void ExtractDeviceId_ReturnsFirstTopicSegment(string topic, string expected)
    {
        Assert.Equal(expected, MqttSubscriberService.ExtractDeviceId(topic));
    }

    [Theory]
    [InlineData("")]
    [InlineData("no-separator")]
    [InlineData("/leading-slash")]
    public void ExtractDeviceId_ReturnsNullWhenThereIsNoDeviceSegment(string topic)
    {
        Assert.Null(MqttSubscriberService.ExtractDeviceId(topic));
    }
}
