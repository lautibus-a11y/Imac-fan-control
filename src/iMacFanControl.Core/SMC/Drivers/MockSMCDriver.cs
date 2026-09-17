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

            if (value == SMCKeys.APPLESMC_CMD_READ || value == SMCKeys.APPLESMC_CMD_GET_KEY_INFO)
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
            else if (_inputBuffer.Count == 5) // 4 key chars + 1 length byte
            {
                byte[] arr = _inputBuffer.ToArray();
                string key = System.Text.Encoding.ASCII.GetString(arr, 0, 4);
                byte len = arr[4];
                RespondMockKey(key, len);
                _status = SMCKeys.SMC_STATUS_READ;
            }
            else
            {
                _status = SMCKeys.SMC_STATUS_WAITING;
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
        switch (key)
        {
            case "#KEY":
                _outputBuffer.Enqueue(0x00);
                _outputBuffer.Enqueue(0x00);
                _outputBuffer.Enqueue(0x01);
                _outputBuffer.Enqueue(0x40); // 320 keys
                break;
            case "FNum":
                _outputBuffer.Enqueue(0x03); // 3 fans on iMac Mid 2011
                break;
            case "F0ID":
                foreach (byte b in System.Text.Encoding.ASCII.GetBytes("ODD Fan\0\0\0\0\0\0\0\0\0"))
                    _outputBuffer.Enqueue(b);
                break;
            case "F0Ac":
            case "F0Tg":
                _outputBuffer.Enqueue(0x12);
                _outputBuffer.Enqueue(0xC0); // 1200 RPM in fpe2
                break;
            case "F0Mn":
                _outputBuffer.Enqueue(0x12);
                _outputBuffer.Enqueue(0xC0); // 1200 RPM
                break;
            case "F0Mx":
                _outputBuffer.Enqueue(0x33);
                _outputBuffer.Enqueue(0x90); // 3300 RPM
                break;
            case "F1ID":
                foreach (byte b in System.Text.Encoding.ASCII.GetBytes("HDD Fan\0\0\0\0\0\0\0\0\0"))
                    _outputBuffer.Enqueue(b);
                break;
            case "F1Ac":
            case "F1Tg":
                _outputBuffer.Enqueue(0x11);
                _outputBuffer.Enqueue(0x30); // 1100 RPM
                break;
            case "F1Mn":
                _outputBuffer.Enqueue(0x11);
                _outputBuffer.Enqueue(0x30); // 1100 RPM
                break;
            case "F1Mx":
                _outputBuffer.Enqueue(0x55);
                _outputBuffer.Enqueue(0xF0); // 5500 RPM
                break;
            case "F2ID":
                foreach (byte b in System.Text.Encoding.ASCII.GetBytes("CPU Fan\0\0\0\0\0\0\0\0\0"))
                    _outputBuffer.Enqueue(b);
                break;
            case "F2Ac":
            case "F2Tg":
                _outputBuffer.Enqueue(0x12);
                _outputBuffer.Enqueue(0xC0); // 1200 RPM
                break;
            case "F2Mn":
                _outputBuffer.Enqueue(0x0E);
                _outputBuffer.Enqueue(0xB0); // 940 RPM
                break;
            case "F2Mx":
                _outputBuffer.Enqueue(0x2A);
                _outputBuffer.Enqueue(0x30); // 2700 RPM
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
