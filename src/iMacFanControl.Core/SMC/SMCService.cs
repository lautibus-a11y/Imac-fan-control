using System;
using System.Collections.Generic;
using iMacFanControl.Core.Models;
using iMacFanControl.Core.SMC.Drivers;

namespace iMacFanControl.Core.SMC;

public class SMCService : ISMCService
{
    private readonly ISMCLowLevelDriver _driver;
    private readonly SMCReader _reader;
    private readonly SMCWriter _writer;

    public bool IsSMCDetected { get; private set; }
    public string DriverName => _driver.DriverName;
    public int TotalSMCKeys { get; private set; }

    public SMCService(ISMCLowLevelDriver driver)
    {
        _driver = driver ?? throw new ArgumentNullException(nameof(driver));
        _reader = new SMCReader(_driver);
        _writer = new SMCWriter(_driver);
    }

    public bool Initialize(out string statusMessage)
    {
        statusMessage = string.Empty;

        // 1. Initialize low-level driver (WinRing0 / InpOut)
        if (!_driver.Initialize(out string driverError))
        {
            statusMessage = $"Driver initialization failed: {driverError}";
            IsSMCDetected = false;
            return false;
        }

        // 2. Probe Apple SMC on I/O ports 0x300 / 0x304 by querying the #KEY register
        if (!_reader.ReadKeyRaw(SMCKeys.KEY_TOTAL_KEYS, 4, out byte[] keyCountBytes))
        {
            statusMessage = "Driver loaded, but no response from Apple SMC on I/O ports 0x300/0x304. " +
                            "Verify this machine is an Intel Mac running Boot Camp.";
            IsSMCDetected = false;
            return false;
        }

        uint keyCount = SMCReader.DecodeUi32(keyCountBytes);
        if (keyCount == 0 || keyCount > 100000)
        {
            statusMessage = $"Invalid SMC response (#KEY returned {keyCount}). SMC not detected.";
            IsSMCDetected = false;
            return false;
        }

        TotalSMCKeys = (int)keyCount;
        IsSMCDetected = true;
        statusMessage = $"Apple SMC detected successfully ({TotalSMCKeys} keys found).";
        return true;
    }

    public List<Fan> GetFans()
    {
        var fans = new List<Fan>();
        if (!IsSMCDetected) return fans;

        // Read fan count
        if (!_reader.ReadKeyRaw(SMCKeys.KEY_FAN_COUNT, 1, out byte[] fanCountBytes))
            return fans;

        byte fanCount = SMCReader.DecodeUi8(fanCountBytes);

        for (int i = 0; i < fanCount; i++)
        {
            var smcFan = new SMCFan(i);
            var fan = new Fan
            {
                Index = i,
                Id = $"FAN_{i}"
            };

            // Read fan description ID
            if (_reader.ReadKeyRaw(smcFan.IdKey, 16, out byte[] idBytes))
            {
                string name = SMCReader.DecodeString(idBytes);
                fan.Name = !string.IsNullOrWhiteSpace(name) ? name : GetDefaultFanName(i);
            }
            else
            {
                fan.Name = GetDefaultFanName(i);
            }

            // Read limits and current RPM
            fan.CurrentRpm = GetFanCurrentRPM(i) ?? 0;
            fan.MinRpm = GetFanMinRPM(i) ?? 0;
            fan.MaxRpm = GetFanMaxRPM(i) ?? 0;
            fan.TargetRpm = GetFanTargetRPM(i) ?? fan.CurrentRpm;
            fan.Mode = GetFanMode(i) ?? FanControlMode.Auto;

            // Safe RPM if present
            if (_reader.ReadKeyRaw(smcFan.SafeRpmKey, 2, out byte[] safeBytes))
            {
                fan.SafeRpm = SMCReader.DecodeFpe2(safeBytes);
            }

            fans.Add(fan);
        }

        return fans;
    }

    private static string GetDefaultFanName(int index) => index switch
    {
        0 => "ODD Fan (Optical)",
        1 => "HDD Fan (Storage)",
        2 => "CPU Fan",
        _ => $"Fan {index}"
    };

    public int? GetFanCurrentRPM(int fanIndex)
    {
        if (!IsSMCDetected) return null;
        string key = $"F{fanIndex}Ac";
        if (_reader.ReadKeyRaw(key, 2, out byte[] data))
        {
            return SMCReader.DecodeFpe2(data);
        }
        return null;
    }

    public int? GetFanMinRPM(int fanIndex)
    {
        if (!IsSMCDetected) return null;
        string key = $"F{fanIndex}Mn";
        if (_reader.ReadKeyRaw(key, 2, out byte[] data))
        {
            return SMCReader.DecodeFpe2(data);
        }
        return null;
    }

    public int? GetFanMaxRPM(int fanIndex)
    {
        if (!IsSMCDetected) return null;
        string key = $"F{fanIndex}Mx";
        if (_reader.ReadKeyRaw(key, 2, out byte[] data))
        {
            return SMCReader.DecodeFpe2(data);
        }
        return null;
    }

    public int? GetFanTargetRPM(int fanIndex)
    {
        if (!IsSMCDetected) return null;
        string key = $"F{fanIndex}Tg";
        if (_reader.ReadKeyRaw(key, 2, out byte[] data))
        {
            return SMCReader.DecodeFpe2(data);
        }
        return null;
    }

    public FanControlMode? GetFanMode(int fanIndex)
    {
        if (!IsSMCDetected) return null;
        string key = $"F{fanIndex}Md";
        if (_reader.ReadKeyRaw(key, 1, out byte[] data))
        {
            byte mode = SMCReader.DecodeUi8(data);
            return mode == 0 ? FanControlMode.Auto : FanControlMode.Manual;
        }
        return FanControlMode.Auto;
    }

    public List<Sensor> GetSensors()
    {
        var sensors = new List<Sensor>();
        if (!IsSMCDetected) return sensors;

        foreach (var kvp in SMCKeys.KnownSensors)
        {
            string key = kvp.Key;
            var (name, type) = kvp.Value;

            var sensor = new Sensor
            {
                Key = key,
                Name = name,
                Type = type
            };

            float? temp = GetSensorTemperature(key);
            if (temp.HasValue && temp.Value > 0 && temp.Value < 128)
            {
                sensor.CurrentTemperature = temp.Value;
                sensor.IsAvailable = true;
                sensors.Add(sensor);
            }
        }

        return sensors;
    }

    public float? GetSensorTemperature(string key)
    {
        if (!IsSMCDetected || string.IsNullOrEmpty(key)) return null;

        if (_reader.ReadKeyRaw(key, 2, out byte[] data))
        {
            float temp = SMCReader.DecodeSp78(data);
            if (temp > 0.0f && temp < 130.0f)
            {
                return temp;
            }
        }

        return null;
    }

    public bool IsWriteUnlocked => !_writer.IsSafetyLocked;

    public void UnlockHardwareWriting(string token)
    {
        _writer.UnlockForTestingOnly(token);
    }

    public void LockHardwareWriting()
    {
        _writer.LockWriting();
    }

    public bool SetFanTargetRPM(int fanIndex, int targetRpm)
    {
        if (!IsSMCDetected) return false;

        // 1. Switch fan mode to manual override so SMC firmware honors the target speed
        try
        {
            byte[] modeData = SMCWriter.EncodeUi8(0x01);
            _writer.WriteKey($"F{fanIndex}Md", modeData);
        }
        catch
        {
            // Some Mac models don't have or require F{i}Md; proceed to set target
        }

        // 2. Write target RPM in fpe2 format
        byte[] data = SMCWriter.EncodeFpe2(targetRpm);
        return _writer.WriteKey($"F{fanIndex}Tg", data);
    }

    public bool SetFanAutoMode(int fanIndex)
    {
        if (!IsSMCDetected) return false;
        byte[] data = SMCWriter.EncodeUi8(0x00);
        return _writer.WriteKey($"F{fanIndex}Md", data);
    }

    public bool RestoreAllFansToAuto()
    {
        if (!IsSMCDetected) return false;
        if (!_reader.ReadKeyRaw(SMCKeys.KEY_FAN_COUNT, 1, out byte[] fanCountBytes))
            return false;

        byte count = SMCReader.DecodeUi8(fanCountBytes);
        bool allSuccess = true;
        for (int i = 0; i < count; i++)
        {
            try
            {
                if (!SetFanAutoMode(i))
                    allSuccess = false;
            }
            catch
            {
                allSuccess = false;
            }
        }
        return allSuccess;
    }

    public byte[]? ReadSMCKey(string key, out string dataType)
    {
        dataType = string.Empty;
        if (!IsSMCDetected) return null;

        if (_reader.ReadKeyInfo(key, out byte size, out dataType))
        {
            if (_reader.ReadKeyRaw(key, size, out byte[] data))
            {
                return data;
            }
        }
        return null;
    }

    public bool WriteSMCKey(string key, byte[] data)
    {
        if (!IsSMCDetected) return false;
        return _writer.WriteKey(key, data);
    }

    public void Close()
    {
        _driver.Close();
        IsSMCDetected = false;
    }
}
