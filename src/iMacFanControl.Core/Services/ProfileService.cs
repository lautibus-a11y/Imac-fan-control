using System.Collections.Generic;
using iMacFanControl.Core.Models;

namespace iMacFanControl.Core.Services;

public class ProfileService
{
    public List<CoolingProfile> GetBuiltInProfiles(List<Fan> detectedFans)
    {
        var profiles = new List<CoolingProfile>();

        // Auto / Normal Profile (Default Apple SMC control)
        var normal = new CoolingProfile
        {
            Id = "profile_normal",
            Name = "Normal (Apple SMC Auto)",
            Description = "Default Apple System Management Controller dynamic cooling.",
            IsBuiltIn = true
        };
        foreach (var fan in detectedFans)
        {
            normal.FanModes[fan.Index] = FanControlMode.Auto;
        }
        profiles.Add(normal);

        // Silent Profile
        var silent = new CoolingProfile
        {
            Id = "profile_silent",
            Name = "Silent",
            Description = "Maintains minimum hardware RPM until thermal thresholds are reached.",
            IsBuiltIn = true
        };
        foreach (var fan in detectedFans)
        {
            silent.FanModes[fan.Index] = FanControlMode.Manual;
            silent.ManualRpms[fan.Index] = fan.MinRpm;
        }
        profiles.Add(silent);

        // Gaming / Performance Profile
        var gaming = new CoolingProfile
        {
            Id = "profile_gaming",
            Name = "Gaming / Heavy Load",
            Description = "Aggressive cooling profile running fans at higher base speeds.",
            IsBuiltIn = true
        };
        foreach (var fan in detectedFans)
        {
            gaming.FanModes[fan.Index] = FanControlMode.Manual;
            // 60% of RPM range above minimum
            int span = fan.MaxRpm - fan.MinRpm;
            gaming.ManualRpms[fan.Index] = fan.MinRpm + (int)(span * 0.6f);
        }
        profiles.Add(gaming);

        return profiles;
    }
}
