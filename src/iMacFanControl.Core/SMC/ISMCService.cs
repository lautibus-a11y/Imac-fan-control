using System.Collections.Generic;
using iMacFanControl.Core.Models;

namespace iMacFanControl.Core.SMC;

public interface ISMCService
{
    bool IsSMCDetected { get; }
    string DriverName { get; }
    int TotalSMCKeys { get; }

    bool Initialize(out string statusMessage);
    void Close();

    List<Fan> GetFans();
    List<Sensor> GetSensors();

    int? GetFanCurrentRPM(int fanIndex);
    int? GetFanMinRPM(int fanIndex);
    int? GetFanMaxRPM(int fanIndex);
    int? GetFanTargetRPM(int fanIndex);
    FanControlMode? GetFanMode(int fanIndex);

    float? GetSensorTemperature(string key);

    bool SetFanTargetRPM(int fanIndex, int targetRpm);
    bool SetFanAutoMode(int fanIndex);
    bool RestoreAllFansToAuto();

    byte[]? ReadSMCKey(string key, out string dataType);
    bool WriteSMCKey(string key, byte[] data);

    bool IsWriteUnlocked { get; }
    void UnlockHardwareWriting(string token);
    void LockHardwareWriting();
}
