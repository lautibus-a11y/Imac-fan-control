using System;
using iMacFanControl.Core.SMC;

namespace iMacFanControl.Core.Services;

public class SafetyService
{
    private readonly ISMCService _smcService;
    private int _consecutiveSensorFailures = 0;
    private const int MaxAllowedFailures = 5;

    public event Action<string>? SafetyAlertTriggered;

    public SafetyService(ISMCService smcService)
    {
        _smcService = smcService ?? throw new ArgumentNullException(nameof(smcService));
        AttachEmergencyHandlers();
    }

    private void AttachEmergencyHandlers()
    {
        AppDomain.CurrentDomain.ProcessExit += (s, e) => EmergencyRestoreAllFansToAuto("Process exit");
        AppDomain.CurrentDomain.UnhandledException += (s, e) => EmergencyRestoreAllFansToAuto("Unhandled application exception");
        try
        {
            Console.CancelKeyPress += (s, e) => EmergencyRestoreAllFansToAuto("Terminal Ctrl+C interrupt");
        }
        catch { }
    }

    /// <summary>
    /// Clamps RPM strictly between the hardware minimum and maximum limits.
    /// </summary>
    public static int ClampRpm(int targetRpm, int minRpm, int maxRpm)
    {
        if (minRpm <= 0) minRpm = 600;
        if (maxRpm <= minRpm) maxRpm = Math.Max(minRpm + 1000, 3000);

        if (targetRpm < minRpm) return minRpm;
        if (targetRpm > maxRpm) return maxRpm;
        return targetRpm;
    }

    /// <summary>
    /// Feed watchdog with sensor status.
    /// If sensors fail for multiple cycles, automatically triggers SMC Auto Mode.
    /// </summary>
    public void RecordSensorCycleResult(bool sensorsReadSuccessfully)
    {
        if (sensorsReadSuccessfully)
        {
            _consecutiveSensorFailures = 0;
        }
        else
        {
            _consecutiveSensorFailures++;
            if (_consecutiveSensorFailures >= MaxAllowedFailures)
            {
                EmergencyRestoreAllFansToAuto($"Sensors unreachable for {_consecutiveSensorFailures} consecutive cycles (Watchdog).");
            }
        }
    }

    public void EmergencyRestoreAllFansToAuto(string reason)
    {
        SafetyAlertTriggered?.Invoke(reason);
        try
        {
            _smcService.RestoreAllFansToAuto();
        }
        catch
        {
            // Suppress secondary exceptions during emergency restore
        }
    }
}
