namespace _03_01_zadanie.Sensors;

public enum DataFaultKind
{
    /// <summary>An active channel reports a value outside the healthy range.</summary>
    OutOfRange,

    /// <summary>A channel absent from <c>sensor_type</c> reports something other than 0.</summary>
    InactiveChannelReportsValue,

    /// <summary>A measurement field is missing from the file altogether.</summary>
    MissingChannel,

    /// <summary><c>sensor_type</c> names a channel this plant does not have.</summary>
    UnknownSensorType
}

public sealed record DataFault(DataFaultKind Kind, string Channel, double? Value)
{
    public string Describe() => Kind switch
    {
        DataFaultKind.OutOfRange                  => $"{Channel}={Value:0.###} outside {SensorChannel.ByName(Channel)?.DescribeRange()}",
        DataFaultKind.InactiveChannelReportsValue  => $"{Channel}={Value:0.###} reported by an inactive channel",
        DataFaultKind.MissingChannel               => $"{Channel} field missing",
        DataFaultKind.UnknownSensorType            => $"sensor_type names unknown channel '{Channel}'",
        _                                          => Kind.ToString()
    };
}

/// <summary>
/// The deterministic half of the answer. Three of the four anomaly kinds defined by the task
/// are pure arithmetic over the declared ranges, so they are settled here for free — the model
/// is never asked anything a comparison operator can decide.
/// </summary>
public static class ReadingValidator
{
    public static IReadOnlyList<DataFault> Validate(SensorReading reading)
    {
        var faults = new List<DataFault>();

        foreach (var unknown in reading.UnknownChannels)
            faults.Add(new DataFault(DataFaultKind.UnknownSensorType, unknown, null));

        foreach (var channel in SensorChannel.All)
        {
            if (!reading.Values.TryGetValue(channel.Name, out var value))
            {
                faults.Add(new DataFault(DataFaultKind.MissingChannel, channel.Name, null));
                continue;
            }

            if (reading.IsActive(channel))
            {
                if (!channel.IsWithinRange(value))
                    faults.Add(new DataFault(DataFaultKind.OutOfRange, channel.Name, value));
            }
            else if (value != 0)
            {
                faults.Add(new DataFault(DataFaultKind.InactiveChannelReportsValue, channel.Name, value));
            }
        }

        return faults;
    }
}
