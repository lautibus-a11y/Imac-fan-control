using System.Collections.Generic;
using iMacFanControl.Core.Models;

namespace iMacFanControl.Core.Configuration;

public class AppSettings
{
    public string ActiveProfileId { get; set; } = "profile_normal";
    public bool StartWithWindows { get; set; } = false;
    public bool MinimizeToTray { get; set; } = true;
    public int SensorPollIntervalMs { get; set; } = 1000;
    public int CurveUpdateIntervalMs { get; set; } = 2500;
    public float TemperatureHysteresis { get; set; } = 2.0f;
    public bool DarkTheme { get; set; } = true;
    public Dictionary<string, string> FanCurveAssignments { get; set; } = new();
    public List<FanCurve> CustomCurves { get; set; } = new();
}
