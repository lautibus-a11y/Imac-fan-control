namespace iMacFanControl.Core.Models;

public class MachineInfo
{
    public string Manufacturer { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string SerialNumber { get; set; } = string.Empty;
    public string BiosVersion { get; set; } = string.Empty;
    public bool IsAppleHardware { get; set; }
    public bool IsSupportedModel { get; set; }
    public string FriendlyModelName { get; set; } = string.Empty;
    public int DetectedFanCount { get; set; }
}
