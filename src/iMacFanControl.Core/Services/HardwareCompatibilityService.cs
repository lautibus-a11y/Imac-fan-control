using System;
using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;
using iMacFanControl.Core.Models;

namespace iMacFanControl.Core.Services;

public class HardwareCompatibilityService
{
    public MachineInfo DetectMachine()
    {
        var info = new MachineInfo();

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            info.Manufacturer = "Non-Windows Host";
            info.Model = "Unknown";
            info.IsAppleHardware = false;
            info.IsSupportedModel = false;
            info.FriendlyModelName = "Non-Windows Platform";
            return info;
        }

        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Manufacturer, Model, SystemFamily FROM Win32_ComputerSystem");
            foreach (ManagementObject obj in searcher.Get())
            {
                info.Manufacturer = obj["Manufacturer"]?.ToString()?.Trim() ?? string.Empty;
                info.Model = obj["Model"]?.ToString()?.Trim() ?? string.Empty;
                break;
            }

            using var biosSearcher = new ManagementObjectSearcher("SELECT SMBIOSBIOSVersion, SerialNumber FROM Win32_BIOS");
            foreach (ManagementObject obj in biosSearcher.Get())
            {
                info.BiosVersion = obj["SMBIOSBIOSVersion"]?.ToString()?.Trim() ?? string.Empty;
                info.SerialNumber = obj["SerialNumber"]?.ToString()?.Trim() ?? string.Empty;
                break;
            }

            info.IsAppleHardware = info.Manufacturer.Contains("Apple", StringComparison.OrdinalIgnoreCase);

            // Match known iMac models
            if (info.Model.Equals("iMac12,1", StringComparison.OrdinalIgnoreCase))
            {
                info.IsSupportedModel = true;
                info.FriendlyModelName = "iMac (21.5-inch, Mid 2011)";
                info.DetectedFanCount = 3;
            }
            else if (info.Model.Equals("iMac12,2", StringComparison.OrdinalIgnoreCase))
            {
                info.IsSupportedModel = true;
                info.FriendlyModelName = "iMac (27-inch, Mid 2011)";
                info.DetectedFanCount = 3;
            }
            else if (info.Model.StartsWith("iMac", StringComparison.OrdinalIgnoreCase))
            {
                info.IsSupportedModel = true; // Other Intel iMacs
                info.FriendlyModelName = $"{info.Model} (Intel iMac)";
                info.DetectedFanCount = 3;
            }
            else if (info.IsAppleHardware)
            {
                info.IsSupportedModel = true;
                info.FriendlyModelName = $"{info.Model} (Apple Intel Hardware)";
            }
            else
            {
                info.FriendlyModelName = $"{info.Manufacturer} {info.Model}";
                info.IsSupportedModel = false;
            }
        }
        catch (Exception ex)
        {
            info.FriendlyModelName = $"Detection Error: {ex.Message}";
        }

        return info;
    }
}
