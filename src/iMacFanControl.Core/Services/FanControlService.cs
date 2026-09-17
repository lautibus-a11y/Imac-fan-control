using System;
using System.Collections.Generic;
using iMacFanControl.Core.Models;
using iMacFanControl.Core.SMC;

namespace iMacFanControl.Core.Services;

public class FanControlService
{
    private readonly ISMCService _smcService;
    private readonly SafetyService _safetyService;

    public FanControlService(ISMCService smcService, SafetyService safetyService)
    {
        _smcService = smcService ?? throw new ArgumentNullException(nameof(smcService));
        _safetyService = safetyService ?? throw new ArgumentNullException(nameof(safetyService));
    }

    public List<Fan> GetFans()
    {
        return _smcService.GetFans();
    }

    public bool SetFanManualRpm(int fanIndex, int targetRpm)
    {
        // Enforce safety clamping with real hardware limits
        int min = _smcService.GetFanMinRPM(fanIndex) ?? 1000;
        int max = _smcService.GetFanMaxRPM(fanIndex) ?? 3000;
        int clamped = SafetyService.ClampRpm(targetRpm, min, max);

        return _smcService.SetFanTargetRPM(fanIndex, clamped);
    }

    public bool RestoreFanToAuto(int fanIndex)
    {
        return _smcService.SetFanAutoMode(fanIndex);
    }

    public bool RestoreAllToAuto()
    {
        return _smcService.RestoreAllFansToAuto();
    }
}
