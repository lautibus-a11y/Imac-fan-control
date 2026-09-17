using System.Collections.Generic;

namespace iMacFanControl.Core.Models;

public class FanCurve
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string AssociatedSensorKey { get; set; } = string.Empty;
    public List<FanCurvePoint> Points { get; set; } = new();
    public float HysteresisDegrees { get; set; } = 2.0f;
}
