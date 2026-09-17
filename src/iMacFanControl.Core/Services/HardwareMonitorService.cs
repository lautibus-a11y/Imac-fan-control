using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using iMacFanControl.Core.Models;
using iMacFanControl.Core.SMC;

namespace iMacFanControl.Core.Services;

public class HardwareMonitorService : IDisposable
{
    private readonly ISMCService _smcService;
    private readonly SensorService _sensorService;
    private readonly SafetyService _safetyService;
    private CancellationTokenSource? _cts;
    private Task? _monitorTask;

    public event Action<List<Sensor>>? SensorsUpdated;
    public event Action<List<Fan>>? FansUpdated;

    public int SensorIntervalMs { get; set; } = 1000;
    public bool IsRunning { get; private set; }

    public HardwareMonitorService(
        ISMCService smcService,
        SensorService sensorService,
        SafetyService safetyService)
    {
        _smcService = smcService;
        _sensorService = sensorService;
        _safetyService = safetyService;
    }

    public void Start()
    {
        if (IsRunning) return;
        IsRunning = true;
        _cts = new CancellationTokenSource();
        _monitorTask = Task.Run(() => MonitorLoopAsync(_cts.Token));
    }

    public void Stop()
    {
        if (!IsRunning) return;
        _cts?.Cancel();
        try
        {
            _monitorTask?.Wait(2000);
        }
        catch
        {
            // Ignore cancel exceptions
        }
        IsRunning = false;
    }

    private async Task MonitorLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                if (_smcService.IsSMCDetected)
                {
                    var sensors = _sensorService.GetAllSensors();
                    _safetyService.RecordSensorCycleResult(sensors.Count > 0);
                    SensorsUpdated?.Invoke(sensors);

                    var fans = _smcService.GetFans();
                    FansUpdated?.Invoke(fans);
                }
                else
                {
                    _safetyService.RecordSensorCycleResult(false);
                }
            }
            catch (Exception ex)
            {
                _safetyService.RecordSensorCycleResult(false);
                Console.Error.WriteLine($"Monitor loop error: {ex.Message}");
            }

            try
            {
                await Task.Delay(SensorIntervalMs, token);
            }
            catch (TaskCanceledException)
            {
                break;
            }
        }
    }

    public void Dispose()
    {
        Stop();
        _cts?.Dispose();
    }
}
