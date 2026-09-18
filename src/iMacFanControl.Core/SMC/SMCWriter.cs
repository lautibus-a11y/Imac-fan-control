using System;
using System.Text;
using System.Threading;
using iMacFanControl.Core.SMC.Drivers;

namespace iMacFanControl.Core.SMC;

/// <summary>
/// Handles writing to the Apple SMC.
/// SAFETY NOTICE: In Phase 1, write operations are strictly locked by default.
/// Hardware writing is only unlocked during Phase 2 after sensor and fan read verification.
/// </summary>
public class SMCWriter
{
    private readonly ISMCLowLevelDriver _driver;
    private readonly object _ioLock = new();
    private const int MaxTimeoutIterations = 40000;

    /// <summary>
    /// Strict safety lock. Must remain true during Phase 1 diagnostic.
    /// </summary>
    public bool IsSafetyLocked { get; private set; } = true;

    public const string ConfirmationToken = "PHASE_2_HARDWARE_WRITE_CONFIRMED";

    public SMCWriter(ISMCLowLevelDriver driver)
    {
        _driver = driver ?? throw new ArgumentNullException(nameof(driver));
    }

    public void UnlockForTestingOnly(string confirmationToken)
    {
        if (confirmationToken == ConfirmationToken)
        {
            IsSafetyLocked = false;
        }
    }

    public void LockWriting()
    {
        IsSafetyLocked = true;
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

    public bool WriteKey(string key, byte[] data)
    {
        if (IsSafetyLocked)
        {
            throw new InvalidOperationException(
                "SAFETY INTERLOCK: SMC writing is disabled in Phase 1. " +
                "Hardware writes can only be activated after successful verification of read diagnostic.");
        }

        if (string.IsNullOrEmpty(key) || key.Length != 4 || data == null || data.Length == 0)
            return false;

        lock (_ioLock)
        {
            // 1. Wait until SMC is not busy
            if (!WaitStatus(SMCKeys.SMC_STATUS_BUSY, 0x00))
                return false;

            // 2. Send WRITE command
            _driver.WritePort8(SMCKeys.APPLESMC_CMD_PORT, SMCKeys.APPLESMC_CMD_WRITE);

            // 3. Send 4-character key
            byte[] keyBytes = Encoding.ASCII.GetBytes(key);
            for (int i = 0; i < 4; i++)
            {
                if (!WaitStatus(SMCKeys.SMC_STATUS_WAITING, SMCKeys.SMC_STATUS_WAITING))
                    return false;

                _driver.WritePort8(SMCKeys.APPLESMC_DATA_PORT, keyBytes[i]);
            }

            // 4. Send data length
            if (!WaitStatus(SMCKeys.SMC_STATUS_WAITING, SMCKeys.SMC_STATUS_WAITING))
                return false;

            _driver.WritePort8(SMCKeys.APPLESMC_DATA_PORT, (byte)data.Length);

            // 5. Send data bytes
            for (int i = 0; i < data.Length; i++)
            {
                if (!WaitStatus(SMCKeys.SMC_STATUS_WAITING, SMCKeys.SMC_STATUS_WAITING))
                    return false;

                _driver.WritePort8(SMCKeys.APPLESMC_DATA_PORT, data[i]);
            }

            return true;
        }
    }

    // Encoder helpers for Phase 2
    public static byte[] EncodeFpe2(int rpm)
    {
        int raw = rpm << 2;
        return new byte[] { (byte)((raw >> 8) & 0xFF), (byte)(raw & 0xFF) };
    }

    public static byte[] EncodeUi8(byte value)
    {
        return new byte[] { value };
    }
}
