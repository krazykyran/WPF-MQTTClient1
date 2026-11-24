using CommunityToolkit.Mvvm.ComponentModel;

namespace WpfMqtt1.Models;

public sealed partial class TopicSubscription : ObservableObject
{
    public string TopicFilter { get; }
    public int Qos { get; }
    public string ColorHex { get; }

    [ObservableProperty]
    private bool _isMuted;

    [ObservableProperty]
    private int _messageCount;

    public string QosLabel => $"QoS {Qos}";

    public TopicSubscription(string topicFilter, int qos, string colorHex)
    {
        TopicFilter = topicFilter;
        Qos = Math.Clamp(qos, 0, 2);
        ColorHex = colorHex;
    }
}
