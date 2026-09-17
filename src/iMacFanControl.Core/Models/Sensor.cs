namespace iMacFanControl.Core.Models;

public enum SensorType
{
    CPU,
    GPU,
    HDD,
    ODD,
    Ambient,
    Memory,
    PowerSupply,
    Motherboard,
    Other
}

public class Sensor
{
    public string Key { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public SensorType Type { get; set; } = SensorType.Other;
    public float CurrentTemperature { get; set; }
    public bool IsAvailable { get; set; }

    public override string ToString() =>
        $"{Name} ({Key}): {(IsAvailable ? $"{CurrentTemperature:F1}°C" : "N/A")}";
}
