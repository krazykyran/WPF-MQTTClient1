namespace WpfMqtt1.Models;

public enum MessageDirection
{
    Inbound = 0,
    Outbound = 1
}

public enum MessageDirectionFilter
{
    All,
    Inbound,
    Outbound
}
