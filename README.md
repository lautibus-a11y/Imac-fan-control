# iMac Fan Control

Native Windows Hardware Monitor & Fan Controller engineered specifically for **Intel-based Apple iMacs running Windows via Boot Camp** (initially targeting the **iMac Mid 2011 - 21.5" & 27"**).

---

## Architecture Overview

The system strictly decouples the UI and control logic from low-level hardware communication:

```
[WinUI 3 / CLI UI]
        ↓
[FanControlService] ── [SafetyService (Clamps & Watchdog)]
        ↓
 [SensorService]
        ↓
  [ISMCService] (SMCService)
        ↓
[SMCReader / SMCWriter]
        ↓
[ISMCLowLevelDriver] (DirectKernelDriver / WinRing0)
        ↓
Apple SMC Hardware (I/O Ports 0x300 / 0x304 on LPC bus)
```

### Folder Structure
- `src/iMacFanControl.Core/`: Core domain models, SMC protocol implementation, services, and configuration.
  - `Models/`: `Fan`, `Sensor`, `FanCurve`, `CoolingProfile`, `MachineInfo`.
  - `SMC/`:
    - `ISMCService.cs`: High-level hardware interface.
    - `SMCService.cs`: Coordinates fan enumeration, sensor polling, and safety locks.
    - `SMCReader.cs`: Protocol decoder for `sp78` (temperature) and `fpe2` (RPM).
    - `SMCWriter.cs`: Protocol encoder (strictly interlocked in Phase 1).
    - `SMCKeys.cs`: Key database for Apple SMC registers (`#KEY`, `FNum`, `F{i}Ac`, `TC0D`, `TG0D`, `TH0P`, etc.).
    - `Drivers/`:
      - `DirectKernelDriver.cs`: Pure Win32 communication with `WinRing0x64.sys` via Service Control Manager & IOCTLs (no external DLL needed).
      - `WinRing0Driver.cs`: Fallback driver using `WinRing0x64.dll`.
      - `InpOutDriver.cs`: Fallback driver using `inpoutx64.dll`.
      - `MockSMCDriver.cs`: Emulated driver for offline test validation (`--mock`).
  - `Services/`:
    - `HardwareCompatibilityService.cs`: WMI detection for Apple iMac hardware.
    - `SensorService.cs`: Dedicated thermal sensor manager.
    - `FanControlService.cs`: Fan monitor and mode controller.
    - `SafetyService.cs`: Hardware clamping, sensor watchdog, and emergency auto-recovery hooks.
    - `ProfileService.cs`: Proportional cooling profiles (Silent, Normal, Gaming).
    - `HardwareMonitorService.cs`: Background asynchronous polling loops.
  - `Configuration/`: `settings.json` and `SettingsManager.cs`.
- `src/iMacFanControl.Diagnostic/`: Standalone console application implementing the **Hardware Diagnostic** tool for Phase 1.
- `src/iMacFanControl.UI/`: Scaffolding for WinUI 3 desktop dashboard (Phase 3).

---

## Phase 1: Hardware Diagnostic

Phase 1 provides a diagnostic tool to verify real communication with the Apple SMC without writing or altering any fan speeds.

### How to Build & Run on Windows (Boot Camp)

1. Open a terminal (PowerShell or Command Prompt) on Windows.
2. Build the diagnostic tool:
   ```cmd
   dotnet build -c Release
   ```
3. Run as **Administrator** (required for low-level kernel I/O):
   ```cmd
   cd src\iMacFanControl.Diagnostic\bin\Release\net8.0-windows
   iMacFanControl.Diagnostic.exe
   ```
   *Alternatively, pass `--watch` to monitor live updates every second:*
   ```cmd
   iMacFanControl.Diagnostic.exe --watch
   ```
   *Or pass `--dump` to see raw SMC hex values:*
   ```cmd
   iMacFanControl.Diagnostic.exe --dump
   ```
