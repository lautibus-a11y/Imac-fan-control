using System;
using System.Collections.Generic;

namespace iMacFanControl.Core.SMC.Drivers;

/// <summary>
/// Mock driver solely used for automated offline test verification when not running on actual Apple hardware.
/// </summary>
public class MockSMCDriver : ISMCLowLevelDriver
{
    public string DriverName => "Mock SMC Driver (Emulated Test Mode)";
    public bool IsLoaded { get; private set; }

    // Emulated ports
    private byte _status = 0x00;
    private byte _lastCommand = 0x00;
    private readonly Queue<byte> _inputBuffer = new();
    private readonly Queue<byte> _outputBuffer = new();

    private class MockFanState
    {
        public string Name { get; set; } = string.Empty;
        public int CurrentRpm { get; set; }
        public int TargetRpm { get; set; }
        public int MinRpm { get; set; }
        public int MaxRpm { get; set; }
        public byte Mode { get; set; } // 0 = Auto, 1 = Manual
    }

    private readonly Dictionary<int, MockFanState> _mockFans = new()
    {
        [0] = new MockFanState { Name = "ODD Fan", CurrentRpm = 1200, TargetRpm = 1200, MinRpm = 1200, MaxRpm = 3300, Mode = 0 },
        [1] = new MockFanState { Name = "HDD Fan", CurrentRpm = 1100, TargetRpm = 1100, MinRpm = 1100, MaxRpm = 5500, Mode = 0 },
        [2] = new MockFanState { Name = "CPU Fan", CurrentRpm = 1200, TargetRpm = 1200, MinRpm = 940, MaxRpm = 2700, Mode = 0 },
    };

    public bool Initialize(out string errorMessage)
    {
        errorMessage = string.Empty;
        IsLoaded = true;
        return true;
    }

    public byte ReadPort8(ushort port)
    {
        if (port == SMCKeys.APPLESMC_CMD_PORT) // 0x304
        {
            if (_outputBuffer.Count > 0)
                return SMCKeys.SMC_STATUS_READ; // 0x04
            return _status;
        }

        if (port == SMCKeys.APPLESMC_DATA_PORT) // 0x300
        {
            if (_outputBuffer.Count > 0)
            {
                byte val = _outputBuffer.Dequeue();
                if (_outputBuffer.Count == 0)
                    _status = 0x00; // Idle
                return val;
            }
            return 0x00;
        }

        return 0x00;
    }

    public void WritePort8(ushort port, byte value)
    {
        if (port == SMCKeys.APPLESMC_CMD_PORT) // 0x304
        {
            _inputBuffer.Clear();
            _outputBuffer.Clear();
            _lastCommand = value;

            if (value == SMCKeys.APPLESMC_CMD_READ || 
                value == SMCKeys.APPLESMC_CMD_GET_KEY_INFO || 
                value == SMCKeys.APPLESMC_CMD_WRITE)
            {
                _status = SMCKeys.SMC_STATUS_WAITING; // 0x02: waiting for input
            }
            else
            {
                _status = 0x00;
            }
        }
        else if (port == SMCKeys.APPLESMC_DATA_PORT) // 0x300
        {
            _inputBuffer.Enqueue(value);

            if (_lastCommand == SMCKeys.APPLESMC_CMD_GET_KEY_INFO && _inputBuffer.Count == 4)
            {
                // GET_KEY_INFO sends 4 bytes (key name)
                byte[] arr = _inputBuffer.ToArray();
                string key = System.Text.Encoding.ASCII.GetString(arr, 0, 4);
                RespondMockKeyInfo(key);
                _status = SMCKeys.SMC_STATUS_READ;
            }
            else if (_lastCommand == SMCKeys.APPLESMC_CMD_READ && _inputBuffer.Count == 5)
            {
                // READ sends 4 bytes (key) + 1 byte (length)
                byte[] arr = _inputBuffer.ToArray();
                string key = System.Text.Encoding.ASCII.GetString(arr, 0, 4);
                byte len = arr[4];
                RespondMockKey(key, len);
                _status = SMCKeys.SMC_STATUS_READ;
            }
            else if (_lastCommand == SMCKeys.APPLESMC_CMD_WRITE && _inputBuffer.Count >= 5)
            {
                // WRITE sends: 4 bytes key, 1 byte length (L), then L data bytes
                byte[] arr = _inputBuffer.ToArray();
                byte len = arr[4];
                if (_inputBuffer.Count == 5 + len)
                {
                    string key = System.Text.Encoding.ASCII.GetString(arr, 0, 4);
                    byte[] data = new byte[len];
                    Array.Copy(arr, 5, data, 0, len);
                    HandleMockWrite(key, data);
                    _status = 0x00; // Idle
                }
                else
                {
                    _status = SMCKeys.SMC_STATUS_WAITING;
                }
            }
            else
            {
                _status = SMCKeys.SMC_STATUS_WAITING;
            }
        }
    }

    private void HandleMockWrite(string key, byte[] data)
    {
        // Handle fan target RPM: F{i}Tg
        if (key.Length == 4 && key.StartsWith("F") && key.EndsWith("Tg") && char.IsDigit(key[1]))
        {
            int fanIndex = key[1] - '0';
            if (_mockFans.TryGetValue(fanIndex, out var fan) && data.Length >= 2)
            {
                int rpm = ((data[0] << 8) | data[1]) >> 2;
                fan.TargetRpm = rpm;
                fan.CurrentRpm = rpm; // In mock, immediately reflect the new target RPM
                fan.Mode = 1; // Manual mode
            }
        }
        // Handle fan mode: F{i}Md
        else if (key.Length == 4 && key.StartsWith("F") && key.EndsWith("Md") && char.IsDigit(key[1]))
        {
            int fanIndex = key[1] - '0';
            if (_mockFans.TryGetValue(fanIndex, out var fan) && data.Length >= 1)
            {
                fan.Mode = data[0];
                if (fan.Mode == 0)
                {
                    // Restored to Auto: return to default base speed
                    fan.TargetRpm = fan.MinRpm;
                    fan.CurrentRpm = fan.MinRpm;
                }
            }
        }
    }

    private void RespondMockKeyInfo(string key)
    {
        _outputBuffer.Clear();

        // Key info is 6 bytes: 1 byte size, 4 bytes type, 1 byte flags
        byte size = 2;
        string type = "sp78";

        if (key == SMCKeys.KEY_TOTAL_KEYS)
        {
            size = 4;
            type = "ui32";
        }
        else if (key == SMCKeys.KEY_FAN_COUNT || key.EndsWith("Md"))
        {
            size = 1;
            type = "ui8 ";
        }
        else if (key.EndsWith("ID"))
        {
            size = 16;
            type = "ch8*";
        }
        else if (key.EndsWith("Ac") || key.EndsWith("Mn") || key.EndsWith("Mx") || key.EndsWith("Tg") || key.EndsWith("Sf"))
        {
            size = 2;
            type = "fpe2";
        }

        _outputBuffer.Enqueue(size);
        byte[] typeBytes = System.Text.Encoding.ASCII.GetBytes(type.PadRight(4));
        for (int i = 0; i < 4; i++)
            _outputBuffer.Enqueue(typeBytes[i]);
        _outputBuffer.Enqueue(0x00); // flags
    }

    private void RespondMockKey(string key, byte len)
    {
        _outputBuffer.Clear();

        // Check if key is a fan query
        if (key.Length == 4 && key.StartsWith("F") && char.IsDigit(key[1]))
        {
            int fanIndex = key[1] - '0';
            if (_mockFans.TryGetValue(fanIndex, out var fan))
            {
                string property = key.Substring(2, 2);
                switch (property)
                {
                    case "ID":
                        byte[] nameBytes = System.Text.Encoding.ASCII.GetBytes(fan.Name.PadRight(16, '\0'));
                        for (int i = 0; i < Math.Min(len, nameBytes.Length); i++)
                            _outputBuffer.Enqueue(nameBytes[i]);
                        return;
                    case "Ac":
                    {
                        int raw = fan.CurrentRpm << 2;
                        _outputBuffer.Enqueue((byte)((raw >> 8) & 0xFF));
                        _outputBuffer.Enqueue((byte)(raw & 0xFF));
                        return;
                    }
                    case "Tg":
                    {
                        int raw = fan.TargetRpm << 2;
                        _outputBuffer.Enqueue((byte)((raw >> 8) & 0xFF));
                        _outputBuffer.Enqueue((byte)(raw & 0xFF));
                        return;
                    }
                    case "Mn":
                    {
                        int raw = fan.MinRpm << 2;
                        _outputBuffer.Enqueue((byte)((raw >> 8) & 0xFF));
                        _outputBuffer.Enqueue((byte)(raw & 0xFF));
                        return;
                    }
                    case "Mx":
                    {
                        int raw = fan.MaxRpm << 2;
                        _outputBuffer.Enqueue((byte)((raw >> 8) & 0xFF));
                        _outputBuffer.Enqueue((byte)(raw & 0xFF));
                        return;
                    }
                    case "Md":
                        _outputBuffer.Enqueue(fan.Mode);
                        return;
                }
            }
        }

        switch (key)
        {
            case "#KEY":
                _outputBuffer.Enqueue(0x00);
                _outputBuffer.Enqueue(0x00);
                _outputBuffer.Enqueue(0x01);
                _outputBuffer.Enqueue(0x40); // 320 keys
                break;
            case "FNum":
                _outputBuffer.Enqueue((byte)_mockFans.Count); // 3 fans on iMac Mid 2011
                break;

            // Thermal Sensors (sp78 format: int8 integer part, uint8 fraction part)
            case "TC0D": // CPU Die / Core 0: 52.5°C (0x34.0x80)
                _outputBuffer.Enqueue(0x34);
                _outputBuffer.Enqueue(0x80);
                break;
            case "TC0P": // CPU Proximity: 46.0°C (0x2E.0x00)
                _outputBuffer.Enqueue(0x2E);
                _outputBuffer.Enqueue(0x00);
                break;
            case "TC0H": // CPU Heatsink: 44.0°C (0x2C.0x00)
                _outputBuffer.Enqueue(0x2C);
                _outputBuffer.Enqueue(0x00);
                break;
            case "TC1D": // CPU Core 1: 51.0°C
                _outputBuffer.Enqueue(0x33);
                _outputBuffer.Enqueue(0x00);
                break;
            case "TC2D": // CPU Core 2: 53.0°C
                _outputBuffer.Enqueue(0x35);
                _outputBuffer.Enqueue(0x00);
                break;
            case "TC3D": // CPU Core 3: 50.0°C
                _outputBuffer.Enqueue(0x32);
                _outputBuffer.Enqueue(0x00);
                break;
            case "TG0D": // GPU Die: 48.0°C (0x30.0x00)
                _outputBuffer.Enqueue(0x30);
                _outputBuffer.Enqueue(0x00);
                break;
            case "TG0P": // GPU Proximity: 45.0°C (0x2D.0x00)
                _outputBuffer.Enqueue(0x2D);
                _outputBuffer.Enqueue(0x00);
                break;
            case "TG0H": // GPU Heatsink: 43.5°C
                _outputBuffer.Enqueue(0x2B);
                _outputBuffer.Enqueue(0x80);
                break;
            case "TH0P": // HDD Bay: 39.0°C (0x27.0x00)
                _outputBuffer.Enqueue(0x27);
                _outputBuffer.Enqueue(0x00);
                break;
            case "TO0P": // Optical Drive Bay: 38.0°C (0x26.0x00)
                _outputBuffer.Enqueue(0x26);
                _outputBuffer.Enqueue(0x00);
                break;
            case "Tm0P": // Memory Controller: 42.0°C
                _outputBuffer.Enqueue(0x2A);
                _outputBuffer.Enqueue(0x00);
                break;
            case "TM0P": // Memory Proximity: 41.0°C
                _outputBuffer.Enqueue(0x29);
                _outputBuffer.Enqueue(0x00);
                break;
            case "TA0P": // Ambient: 24.0°C
                _outputBuffer.Enqueue(0x18);
                _outputBuffer.Enqueue(0x00);
                break;
            case "Tp0C": // Power Supply: 49.0°C
                _outputBuffer.Enqueue(0x31);
                _outputBuffer.Enqueue(0x00);
                break;
            case "TL0P": // LCD Panel: 35.0°C
                _outputBuffer.Enqueue(0x23);
                _outputBuffer.Enqueue(0x00);
                break;
            default:
                if (key.StartsWith("T") && len == 2)
                {
                    // Generic thermal sensor: 40.0°C
                    _outputBuffer.Enqueue(0x28);
                    _outputBuffer.Enqueue(0x00);
                }
                else
                {
                    for (int i = 0; i < len; i++)
                        _outputBuffer.Enqueue(0x00);
                }
                break;
        }
    }

    public void Close()
    {
        IsLoaded = false;
        _inputBuffer.Clear();
        _outputBuffer.Clear();
    }
}
