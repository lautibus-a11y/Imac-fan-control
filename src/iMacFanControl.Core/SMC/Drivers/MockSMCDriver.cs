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
    private byte _data = 0x00;
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
        if (port == 0x304)
        {
            // If output buffer has bytes, report SMC_STATUS_READ (0x04)
            if (_outputBuffer.Count > 0)
                return 0x04;
            return 0x00;
        }

        if (port == 0x300)
        {
            if (_outputBuffer.Count > 0)
                return _outputBuffer.Dequeue();
            return 0x00;
        }

        return 0x00;
    }

    public void WritePort8(ushort port, byte value)
    {
        if (port == 0x304)
        {
            // Command port
            _inputBuffer.Clear();
            _outputBuffer.Clear();
            if (value == 0x10) // READ
            {
                _status = 0x02; // Await data
            }
        }
        else if (port == 0x300)
        {
            // Data port
            _inputBuffer.Enqueue(value);
            if (_inputBuffer.Count == 5) // 4 key chars + 1 length byte
            {
                byte[] arr = _inputBuffer.ToArray();
                string key = System.Text.Encoding.ASCII.GetString(arr, 0, 4);
                byte len = arr[4];
                RespondMockKey(key, len);
            }
        }
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
                foreach (byte b in System.Text.Encoding.ASCII.GetBytes("ODD Fan\0\0\0\0\0"))
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
                foreach (byte b in System.Text.Encoding.ASCII.GetBytes("HDD Fan\0\0\0\0\0"))
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
                foreach (byte b in System.Text.Encoding.ASCII.GetBytes("CPU Fan\0\0\0\0\0"))
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
            case "TC0D": // CPU Die 52.5°C
                _outputBuffer.Enqueue(0x34);
                _outputBuffer.Enqueue(0x80);
                break;
            case "TG0D": // GPU Die 48.0°C
                _outputBuffer.Enqueue(0x30);
                _outputBuffer.Enqueue(0x00);
                break;
            case "TH0P": // HDD Proximity 39.0°C
                _outputBuffer.Enqueue(0x27);
                _outputBuffer.Enqueue(0x00);
                break;
            case "TO0P": // ODD Proximity 38.0°C
                _outputBuffer.Enqueue(0x26);
                _outputBuffer.Enqueue(0x00);
                break;
            default:
                for (int i = 0; i < len; i++)
                    _outputBuffer.Enqueue(0x00);
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
