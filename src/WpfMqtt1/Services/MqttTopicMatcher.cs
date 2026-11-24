namespace WpfMqtt1.Services;

/// <summary>
/// MQTT 3.1.1 / 5.0 topic filter matching supporting single-level ('+') and multi-level ('#') wildcards.
/// </summary>
public static class MqttTopicMatcher
{
    public static bool IsMatch(string topicFilter, string topic)
    {
        if (string.IsNullOrEmpty(topicFilter) || string.IsNullOrEmpty(topic))
        {
            return false;
        }

        // Per MQTT spec 4.7.2: wildcards at the root must not match topics starting with '$'.
        if (topic[0] == '$' && (topicFilter[0] == '#' || topicFilter[0] == '+'))
        {
            return false;
        }

        var filterLevels = topicFilter.Split('/');
        var topicLevels = topic.Split('/');

        var f = 0;
        var t = 0;

        while (f < filterLevels.Length && t < topicLevels.Length)
        {
            var filterLevel = filterLevels[f];

            if (filterLevel == "#")
            {
                return f == filterLevels.Length - 1;
            }

            if (filterLevel != "+" && !string.Equals(filterLevel, topicLevels[t], StringComparison.Ordinal))
            {
                return false;
            }

            f++;
            t++;
        }

        if (f == filterLevels.Length && t == topicLevels.Length)
        {
            return true;
        }

        // "sport/tennis/#" also matches "sport/tennis" (parent level) per MQTT spec 4.7.1.2.
        if (t == topicLevels.Length && f == filterLevels.Length - 1 && filterLevels[f] == "#")
        {
            return true;
        }

        return false;
    }
}
