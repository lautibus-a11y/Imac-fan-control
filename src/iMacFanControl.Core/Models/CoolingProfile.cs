using System.Collections.Generic;

namespace iMacFanControl.Core.Models;

public class CoolingProfile
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool IsBuiltIn { get; set; }
    public Dictionary<int, FanControlMode> FanModes { get; set; } = new();
    public Dictionary<int, int> ManualRpms { get; set; } = new();
    public Dictionary<int, string> AssignedCurveIds { get; set; } = new();
}
