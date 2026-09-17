using System;
using System.Diagnostics;
using System.Text;
using System.Threading;
using iMacFanControl.Core.SMC.Drivers;

namespace iMacFanControl.Core.SMC;

public class SMCReader
{
    private readonly ISMCLowLevelDriver _driver;
    private readonly object _ioLock = new();
    private const int MaxTimeoutIterations = 40000; // ~40ms timeout

    public SMCReader(ISMCLowLevelDriver driver)
    {
        _driver = driver ?? throw new ArgumentNullException(nameof(driver));
    }

    private bool WaitStatus(byte mask, byte expectedValue)
    {
        for (int i = 0; i < MaxTimeoutIterations; i++)
        {
            byte status = _driver.ReadPort8(SMCKeys.APPLESMC_CMD_PORT);
            if ((status & mask) == expectedValue)
                return true;

            if (i > 100)
                Thread.SpinWait(10);
        }
        return false;
    }

    public bool ReadKeyInfo(string key, out byte dataSize, out string dataType)
    {
        dataSize = 0;
        dataType = string.Empty;

        if (string.IsNullOrEmpty(key) || key.Length != 4)
            return false;

        lock (_ioLock)
        {
            // 1. Wait until SMC is not busy
            if (!WaitStatus(SMCKeys.SMC_STATUS_BUSY, 0x00))
                return false;

            // 2. Send GET_KEY_INFO command
            _driver.WritePort8(SMCKeys.APPLESMC_CMD_PORT, SMCKeys.APPLESMC_CMD_GET_KEY_INFO);

            // 3. Write 4-character key
            byte[] keyBytes = Encoding.ASCII.GetBytes(key);
            for (int i = 0; i < 4; i++)
            {
                if (!WaitStatus(SMCKeys.SMC_STATUS_WAITING, SMCKeys.SMC_STATUS_WAITING))
                    return false;

                _driver.WritePort8(SMCKeys.APPLESMC_DATA_PORT, keyBytes[i]);
            }

            // 4. Read response: 1 byte size, 4 bytes type, 1 byte flags (total 6 bytes)
            byte[] infoBytes = new byte[6];
            for (int i = 0; i < 6; i++)
            {
                if (!WaitStatus(SMCKeys.SMC_STATUS_READ, SMCKeys.SMC_STATUS_READ))
                    return false;

                infoBytes[i] = _driver.ReadPort8(SMCKeys.APPLESMC_DATA_PORT);
            }

            dataSize = infoBytes[0];
            dataType = Encoding.ASCII.GetString(infoBytes, 1, 4).Trim();
            return true;
        }
    }

    public bool ReadKeyRaw(string key, byte expectedSize, out byte[] data)
    {
        data = Array.Empty<byte>();

        if (string.IsNullOrEmpty(key) || key.Length != 4)
            return false;

        lock (_ioLock)
        {
            // 1. Wait until SMC is not busy
            if (!WaitStatus(SMCKeys.SMC_STATUS_BUSY, 0x00))
                return false;

            // 2. Send READ command
            _driver.WritePort8(SMCKeys.APPLESMC_CMD_PORT, SMCKeys.APPLESMC_CMD_READ);

            // 3. Send 4-character key
            byte[] keyBytes = Encoding.ASCII.GetBytes(key);
            for (int i = 0; i < 4; i++)
            {
                if (!WaitStatus(SMCKeys.SMC_STATUS_WAITING, SMCKeys.SMC_STATUS_WAITING))
                    return false;

                _driver.WritePort8(SMCKeys.APPLESMC_DATA_PORT, keyBytes[i]);
            }

            // 4. Send expected size
            if (!WaitStatus(SMCKeys.SMC_STATUS_WAITING, SMCKeys.SMC_STATUS_WAITING))
                return false;

            _driver.WritePort8(SMCKeys.APPLESMC_DATA_PORT, expectedSize);

            // 5. Read data bytes
            byte[] buffer = new byte[expectedSize];
            for (int i = 0; i < expectedSize; i++)
            {
                if (!WaitStatus(SMCKeys.SMC_STATUS_READ, SMCKeys.SMC_STATUS_READ))
                    return false;

                buffer[i] = _driver.ReadPort8(SMCKeys.APPLESMC_DATA_PORT);
            }

            data = buffer;
            return true;
        }
    }

    // Decoders for Apple SMC Data Types

    public static int DecodeFpe2(byte[] bytes)
    {
        if (bytes == null || bytes.Length < 2) return 0;
        // fpe2 format: 14 bits integer, 2 bits fraction (unsigned fixed point)
        int raw = (bytes[0] << 8) | bytes[1];
        return raw >> 2;
    }

    public static float DecodeSp78(byte[] bytes)
    {
        if (bytes == null || bytes.Length < 2) return 0.0f;
        // sp78 format: 1 bit sign, 7 bits integer, 8 bits fraction (signed fixed point)
        short raw = (short)((bytes[0] << 8) | bytes[1]);
        return raw / 256.0f;
    }

    public static byte DecodeUi8(byte[] bytes)
    {
        if (bytes == null || bytes.Length < 1) return 0;
        return bytes[0];
    }

    public static ushort DecodeUi16(byte[] bytes)
    {
        if (bytes == null || bytes.Length < 2) return 0;
        return (ushort)((bytes[0] << 8) | bytes[1]);
    }

    public static uint DecodeUi32(byte[] bytes)
    {
        if (bytes == null || bytes.Length < 4) return 0;
        return ((uint)bytes[0] << 24) | ((uint)bytes[1] << 16) | ((uint)bytes[2] << 8) | (uint)bytes[3];
    }

    public static float DecodeFloat(byte[] bytes)
    {
        if (bytes == null || bytes.Length < 4) return 0.0f;
        byte[] copy = new byte[4];
        // Apple SMC is big-endian, bitconverter on x86 is little-endian
        if (BitConverter.IsLittleEndian)
        {
            copy[0] = bytes[3];
            copy[1] = bytes[2];
            copy[2] = bytes[1];
            copy[3] = bytes[0];
        }
        else
        {
            Array.Copy(bytes, copy, 4);
        }
        return BitConverter.ToSingle(copy, 0);
    }

    public static string DecodeString(byte[] bytes)
    {
        if (bytes == null || bytes.Length == 0) return string.Empty;
        string s = Encoding.ASCII.GetString(bytes);
        int nullIdx = s.IndexOf('\0');
        if (nullIdx >= 0)
            s = s.Substring(0, nullIdx);
        return s.Trim();
    }
}
