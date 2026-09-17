using iMacFanControl.Core.Models;

namespace iMacFanControl.Core.SMC;

public class SMCSensor
{
    public string Key { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public SensorType Type { get; set; } = SensorType.Other;
    public string ExpectedDataType { get; set; } = "sp78";

    public SMCSensor(string key, string name, SensorType type, string dataType = "sp78")
    {
        Key = key;
        Name = name;
        Type = type;
        ExpectedDataType = dataType;
    }
}
