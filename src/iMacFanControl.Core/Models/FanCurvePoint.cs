namespace iMacFanControl.Core.Models;

public class FanCurvePoint
{
    public float Temperature { get; set; }
    public int TargetRpm { get; set; }

    public FanCurvePoint() { }

    public FanCurvePoint(float temperature, int targetRpm)
    {
        Temperature = temperature;
        TargetRpm = targetRpm;
    }
}
