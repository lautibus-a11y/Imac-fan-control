using System.Collections.Generic;
using System.Linq;
using iMacFanControl.Core.Models;
using iMacFanControl.Core.SMC;

namespace iMacFanControl.Core.Services;

public class SensorService
{
    private readonly ISMCService _smcService;

    public SensorService(ISMCService smcService)
    {
        _smcService = smcService;
    }

    public List<Sensor> GetAllSensors()
    {
        return _smcService.GetSensors();
    }

    public float? GetCpuTemperature()
    {
        // Try CPU Die first (TC0D), then CPU Proximity (TC0P), then Heatsink (TC0H)
        return _smcService.GetSensorTemperature("TC0D")
            ?? _smcService.GetSensorTemperature("TC0P")
            ?? _smcService.GetSensorTemperature("TC0H");
    }

    public float? GetGpuTemperature()
    {
        // Try GPU Die (TG0D), then GPU Proximity (TG0P)
        return _smcService.GetSensorTemperature("TG0D")
            ?? _smcService.GetSensorTemperature("TG0P")
            ?? _smcService.GetSensorTemperature("TG0H");
    }

    public float? GetHddTemperature()
    {
        return _smcService.GetSensorTemperature("TH0P");
    }

    public float? GetOpticalDriveTemperature()
    {
        return _smcService.GetSensorTemperature("TO0P");
    }

    public float? GetAmbientTemperature()
    {
        return _smcService.GetSensorTemperature("TA0P");
    }
}
