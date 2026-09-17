namespace iMacFanControl.Core.Models;

public enum FanControlMode
{
    Auto = 0,
    Manual = 1,
    Curve = 2
}

public class Fan
{
    public int Index { get; set; }
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int CurrentRpm { get; set; }
    public int TargetRpm { get; set; }
    public int MinRpm { get; set; }
    public int MaxRpm { get; set; }
    public int SafeRpm { get; set; }
    public FanControlMode Mode { get; set; } = FanControlMode.Auto;
    public bool IsControlled { get; set; }

    public override string ToString() =>
        $"{Name} (#{Index}): {CurrentRpm} RPM (Min: {MinRpm}, Max: {MaxRpm}, Mode: {Mode})";
}
