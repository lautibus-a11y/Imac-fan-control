using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading;
using iMacFanControl.Core.Models;
using iMacFanControl.Core.Services;
using iMacFanControl.Core.SMC;
using iMacFanControl.Core.SMC.Drivers;

namespace iMacFanControl.Diagnostic;

internal class Program
{
    private static void Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.Title = "iMac Fan Control - Hardware Diagnostic & Controller (Phase 2)";

        PrintHeader();

        bool useMock = HasArg(args, "--mock");
        bool watchMode = HasArg(args, "--watch") || HasArg(args, "-w");
        bool dumpKeys = HasArg(args, "--dump");
        bool interactiveMode = HasArg(args, "--interactive") || HasArg(args, "-i");
        bool restoreAuto = HasArg(args, "--auto");
        string? profileArg = GetArgValue(args, "--profile") ?? GetArgValue(args, "-p");
        bool hasSetFan = GetSetFanArgs(args, out int targetFanIndex, out int targetRpm);

        // 1. Operating System & Admin Privilege Check
        CheckPlatformAndPrivileges(useMock);

        // 2. Detect Machine Hardware
        var compatService = new HardwareCompatibilityService();
        var machineInfo = compatService.DetectMachine();
        PrintMachineInfo(machineInfo);

        // 3. Select and Initialize SMC Driver
        ISMCLowLevelDriver driver = SelectDriver(useMock);
        var smcService = new SMCService(driver);
        var safetyService = new SafetyService(smcService);
        var fanControlService = new FanControlService(smcService, safetyService);
        var profileService = new ProfileService();

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("\n[1/3] Initializing SMC Communication Layer...");
        Console.ResetColor();

        bool smcInitSuccess = smcService.Initialize(out string statusMessage);

        Console.Write("SMC Status: ");
        if (smcInitSuccess)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("Detected");
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine($"      Detail: {statusMessage}");
            Console.WriteLine($"      Driver: {smcService.DriverName}");
            Console.ResetColor();
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("Not detected");
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"      Detail: {statusMessage}");
            Console.ResetColor();

            PrintDriverTroubleshootingGuide();
            if (!useMock)
            {
                Console.WriteLine("\nPress any key to exit...");
                try { Console.ReadKey(); } catch { }
                return;
            }
        }

        // Handle specific CLI actions if provided
        if (hasSetFan)
        {
            ExecuteSetFan(smcService, fanControlService, targetFanIndex, targetRpm);
            smcService.Close();
            return;
        }

        if (restoreAuto)
        {
            ExecuteRestoreAuto(smcService, fanControlService);
            smcService.Close();
            return;
        }

        if (!string.IsNullOrEmpty(profileArg))
        {
            ExecuteApplyProfile(smcService, fanControlService, profileService, profileArg);
            smcService.Close();
            return;
        }

        if (interactiveMode)
        {
            RunInteractiveMenu(smcService, fanControlService, profileService);
            smcService.Close();
            return;
        }

        // 4. Run Diagnostic Query
        RunDiagnosticScan(smcService, dumpKeys);

        // 5. If watch mode is active or user wants continuous monitor
        if (watchMode)
        {
            RunLiveWatchLoop(smcService);
        }
        else
        {
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("--- AVAILABLE COMMANDS ---");
            Console.ResetColor();
            Console.WriteLine("  --interactive / -i       Open interactive fan control menu");
            Console.WriteLine("  --set-fan <index> <rpm>  Set manual RPM for a fan (e.g. --set-fan 1 1800)");
            Console.WriteLine("  --profile <name>         Apply cooling profile (silent | normal | gaming)");
            Console.WriteLine("  --auto                   Restore all fans to native Apple SMC control");
            Console.WriteLine("  --watch / -w             Monitor live temperatures and RPM in real time");
            Console.WriteLine("  --dump                   Dump all raw SMC registers in hex");
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("Press 'I' to open Interactive Control Menu, or any other key to exit...");
            Console.ResetColor();

            try
            {
                var key = Console.ReadKey();
                if (key.Key == ConsoleKey.I)
                {
                    Console.Clear();
                    RunInteractiveMenu(smcService, fanControlService, profileService);
                }
            }
            catch { }
        }

        smcService.Close();
    }

    private static void ExecuteSetFan(SMCService smcService, FanControlService fanControlService, int fanIndex, int targetRpm)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"\n[Action: Set Fan Speed]");
        Console.ResetColor();

        var fans = smcService.GetFans();
        if (fanIndex < 0 || fanIndex >= fans.Count)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Error: Invalid fan index {fanIndex}. Detected {fans.Count} fans (0 to {fans.Count - 1}).");
            Console.ResetColor();
            return;
        }

        var fan = fans[fanIndex];
        int min = fan.MinRpm > 0 ? fan.MinRpm : 1000;
        int max = fan.MaxRpm > 0 ? fan.MaxRpm : 5500;
        int clampedRpm = SafetyService.ClampRpm(targetRpm, min, max);

        if (clampedRpm != targetRpm)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"Notice: Requested {targetRpm} RPM clamped to safe hardware range [{min} - {max} RPM] -> {clampedRpm} RPM.");
            Console.ResetColor();
        }

        // Unlock SMC writing
        smcService.UnlockHardwareWriting(SMCWriter.ConfirmationToken);

        Console.Write($"Setting {fan.Name} [Index {fanIndex}] to {clampedRpm} RPM... ");
        bool success = fanControlService.SetFanManualRpm(fanIndex, clampedRpm);

        if (success)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("SUCCESS");
            Console.ResetColor();
            Console.WriteLine($"Mode changed to Manual. Target set to {clampedRpm} RPM.");
            int? updatedCurrent = smcService.GetFanCurrentRPM(fanIndex);
            if (updatedCurrent.HasValue)
            {
                Console.WriteLine($"Current physical speed: {updatedCurrent.Value} RPM");
            }
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("FAILED");
            Console.ResetColor();
            Console.WriteLine("Could not write target RPM to SMC register.");
        }
    }

    private static void ExecuteRestoreAuto(SMCService smcService, FanControlService fanControlService)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"\n[Action: Restore Apple SMC Auto Control]");
        Console.ResetColor();

        smcService.UnlockHardwareWriting(SMCWriter.ConfirmationToken);
        bool success = fanControlService.RestoreAllToAuto();

        if (success)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("SUCCESS: All fans restored to factory Apple SMC automatic control.");
            Console.ResetColor();
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("FAILED: Could not restore one or more fans to auto mode.");
            Console.ResetColor();
        }
    }

    private static void ExecuteApplyProfile(SMCService smcService, FanControlService fanControlService, ProfileService profileService, string profileName)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"\n[Action: Apply Cooling Profile '{profileName}']");
        Console.ResetColor();

        var fans = smcService.GetFans();
        var profiles = profileService.GetBuiltInProfiles(fans);

        CoolingProfile? matched = null;
        foreach (var p in profiles)
        {
            if (p.Name.Contains(profileName, StringComparison.OrdinalIgnoreCase) ||
                p.Id.Contains(profileName, StringComparison.OrdinalIgnoreCase))
            {
                matched = p;
                break;
            }
        }

        if (matched == null)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Unknown profile '{profileName}'. Available profiles:");
            foreach (var p in profiles)
                Console.WriteLine($"  - {p.Name} ({p.Id})");
            Console.ResetColor();
            return;
        }

        smcService.UnlockHardwareWriting(SMCWriter.ConfirmationToken);
        Console.WriteLine($"Applying '{matched.Name}' ({matched.Description})...\n");

        foreach (var fan in fans)
        {
            if (matched.FanModes.TryGetValue(fan.Index, out var mode))
            {
                if (mode == FanControlMode.Auto)
                {
                    fanControlService.RestoreFanToAuto(fan.Index);
                    Console.WriteLine($"  {fan.Name,-15}: Restored to Auto");
                }
                else if (matched.ManualRpms.TryGetValue(fan.Index, out int targetRpm))
                {
                    fanControlService.SetFanManualRpm(fan.Index, targetRpm);
                    Console.WriteLine($"  {fan.Name,-15}: Set to {targetRpm} RPM (Manual)");
                }
            }
        }

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"\nProfile '{matched.Name}' applied successfully.");
        Console.ResetColor();
    }

    private static void RunInteractiveMenu(SMCService smcService, FanControlService fanControlService, ProfileService profileService)
    {
        bool running = true;
        smcService.UnlockHardwareWriting(SMCWriter.ConfirmationToken);

        while (running)
        {
            PrintHeader();
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("======================= INTERACTIVE CONTROL MENU =======================");
            Console.ResetColor();
            Console.WriteLine("  [1] Ver estado actual (RPM y temperaturas en vivo)");
            Console.WriteLine("  [2] Ajustar velocidad manual de un ventilador");
            Console.WriteLine("  [3] Aplicar perfil de refrigeración (Silencioso, Normal, Gaming)");
            Console.WriteLine("  [4] Restaurar TODOS los ventiladores a Automático (SMC de fábrica)");
            Console.WriteLine("  [5] Modo Monitor en vivo (pantalla continua)");
            Console.WriteLine("  [6] Salir");
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("========================================================================");
            Console.ResetColor();
            Console.Write("Selecciona una opción [1-6]: ");

            string? choice = Console.ReadLine()?.Trim();
            Console.WriteLine();

            switch (choice)
            {
                case "1":
                    RunDiagnosticScan(smcService, false);
                    Pause();
                    break;
                case "2":
                    PromptSetFanManual(smcService, fanControlService);
                    Pause();
                    break;
                case "3":
                    PromptApplyProfile(smcService, fanControlService, profileService);
                    Pause();
                    break;
                case "4":
                    ExecuteRestoreAuto(smcService, fanControlService);
                    Pause();
                    break;
                case "5":
                    RunLiveWatchLoop(smcService);
                    break;
                case "6":
                    running = false;
                    break;
                default:
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine("Opción no válida. Ingresa un número del 1 al 6.");
                    Console.ResetColor();
                    Pause();
                    break;
            }
        }
    }

    private static void PromptSetFanManual(SMCService smcService, FanControlService fanControlService)
    {
        var fans = smcService.GetFans();
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("Ventiladores disponibles:");
        Console.ResetColor();
        foreach (var fan in fans)
        {
            Console.WriteLine($"  [{fan.Index}] {fan.Name} - Actual: {fan.CurrentRpm} RPM (Rango seguro: {fan.MinRpm} - {fan.MaxRpm} RPM)");
        }

        Console.Write("\nIngresa el índice del ventilador: ");
        if (!int.TryParse(Console.ReadLine(), out int index) || index < 0 || index >= fans.Count)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("Índice de ventilador no válido.");
            Console.ResetColor();
            return;
        }

        var selected = fans[index];
        Console.Write($"Ingresa las RPM deseadas para '{selected.Name}' [{selected.MinRpm} - {selected.MaxRpm}]: ");
        if (!int.TryParse(Console.ReadLine(), out int rpm))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("Valor de RPM no válido.");
            Console.ResetColor();
            return;
        }

        ExecuteSetFan(smcService, fanControlService, index, rpm);
    }

    private static void PromptApplyProfile(SMCService smcService, FanControlService fanControlService, ProfileService profileService)
    {
        var fans = smcService.GetFans();
        var profiles = profileService.GetBuiltInProfiles(fans);

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("Perfiles disponibles:");
        Console.ResetColor();
        for (int i = 0; i < profiles.Count; i++)
        {
            Console.WriteLine($"  [{i + 1}] {profiles[i].Name} - {profiles[i].Description}");
        }

        Console.Write("\nSelecciona el perfil [1-3]: ");
        if (!int.TryParse(Console.ReadLine(), out int idx) || idx < 1 || idx > profiles.Count)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("Opción de perfil no válida.");
            Console.ResetColor();
            return;
        }

        var selectedProfile = profiles[idx - 1];
        ExecuteApplyProfile(smcService, fanControlService, profileService, selectedProfile.Id);
    }

    private static void Pause()
    {
        Console.WriteLine("\nPresiona Enter para continuar...");
        try { Console.ReadLine(); } catch { }
    }

    private static void PrintHeader()
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine(@"===============================================================================");
        Console.WriteLine(@"                  iMac Fan Control - Hardware Controller                       ");
        Console.WriteLine(@"                  Phase 2: Verified SMC Control & Profiles                     ");
        Console.WriteLine(@"===============================================================================");
        Console.ResetColor();
    }

    private static void CheckPlatformAndPrivileges(bool isMock)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            bool isAdmin = false;
            try
            {
                using var identity = WindowsIdentity.GetCurrent();
                var principal = new WindowsPrincipal(identity);
                isAdmin = principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch
            {
                // Ignore
            }

            Console.Write("Privileges: ");
            if (isAdmin)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("Administrator (Elevated)");
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("Standard User (NOT Elevated)");
                if (!isMock)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("  [!] WARNING: Low-level hardware I/O drivers (WinRing0) require Administrator rights.");
                    Console.WriteLine("  [!] If SMC detection fails, right-click the console/exe and select 'Run as administrator'.");
                }
            }
            Console.ResetColor();
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"Running on non-Windows environment ({RuntimeInformation.OSDescription}).");
            Console.ResetColor();
        }
    }

    private static void PrintMachineInfo(MachineInfo info)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("\n[Machine]");
        Console.ResetColor();
        Console.WriteLine($"  Model:            {info.Model} ({info.FriendlyModelName})");
        Console.WriteLine($"  Manufacturer:     {info.Manufacturer}");
        if (!string.IsNullOrEmpty(info.BiosVersion))
            Console.WriteLine($"  BIOS / Firmware:  {info.BiosVersion}");

        Console.Write("  Compatibility:    ");
        if (info.IsSupportedModel)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("Compatible Intel Mac detected");
        }
        else if (info.IsAppleHardware)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("Apple hardware detected (Unverified model)");
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.DarkYellow;
            Console.WriteLine("Generic PC / Virtual Machine");
        }
        Console.ResetColor();
    }

    private static ISMCLowLevelDriver SelectDriver(bool useMock)
    {
        if (useMock)
        {
            Console.ForegroundColor = ConsoleColor.Magenta;
            Console.WriteLine("\n  [MOCK MODE ACTIVATED: Using simulated SMC driver for dry-run verification]");
            Console.ResetColor();
            return new MockSMCDriver();
        }

        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        string sysPath = Path.Combine(baseDir, "WinRing0x64.sys");
        string winRing0Dll = Path.Combine(baseDir, "WinRing0x64.dll");
        string inpOutDll = Path.Combine(baseDir, "inpoutx64.dll");

        // Prioritize DirectKernelDriver if WinRing0x64.sys is present
        if (File.Exists(sysPath))
        {
            return new DirectKernelDriver();
        }
        else if (File.Exists(winRing0Dll))
        {
            return new WinRing0Driver();
        }
        else if (File.Exists(inpOutDll))
        {
            return new InpOutDriver();
        }

        // Default to DirectKernelDriver (will output descriptive missing file error if sys is not found)
        return new DirectKernelDriver();
    }

    private static void RunDiagnosticScan(SMCService smc, bool dumpRaw)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("\n[2/3] Probing Fans and Hardware Limits...");
        Console.ResetColor();

        var fans = smc.GetFans();
        Console.WriteLine($"Fans detected: {fans.Count}");
        Console.WriteLine("-------------------------------------------------------------------------------");

        if (fans.Count == 0)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("  No fans reported by SMC key 'FNum'.");
            Console.ResetColor();
        }
        else
        {
            foreach (var fan in fans)
            {
                Console.ForegroundColor = ConsoleColor.White;
                Console.WriteLine($"{fan.Name} [Index {fan.Index}]:");
                Console.ResetColor();
                Console.WriteLine($"  Current: {fan.CurrentRpm} RPM");
                Console.WriteLine($"  Minimum: {fan.MinRpm} RPM");
                Console.WriteLine($"  Maximum: {fan.MaxRpm} RPM");
                Console.WriteLine($"  Target:  {fan.TargetRpm} RPM");
                Console.WriteLine($"  Mode:    {fan.Mode}");
                Console.WriteLine();
            }
        }

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("[3/3] Probing Thermal Sensors...");
        Console.ResetColor();
        Console.WriteLine("-------------------------------------------------------------------------------");

        var sensors = smc.GetSensors();
        if (sensors.Count == 0)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("  No active sensors answered the query.");
            Console.ResetColor();
        }
        else
        {
            Console.WriteLine($"{"Sensor Key",-12} | {"Description",-26} | {"Temperature",-12}");
            Console.WriteLine(new string('-', 56));
            foreach (var s in sensors)
            {
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.Write($"{s.Key,-12} | ");
                Console.ForegroundColor = ConsoleColor.White;
                Console.Write($"{s.Name,-26} | ");

                if (s.CurrentTemperature > 75)
                    Console.ForegroundColor = ConsoleColor.Red;
                else if (s.CurrentTemperature > 60)
                    Console.ForegroundColor = ConsoleColor.Yellow;
                else
                    Console.ForegroundColor = ConsoleColor.Green;

                Console.WriteLine($"{s.CurrentTemperature,5:F1} °C");
                Console.ResetColor();
            }
        }

        if (dumpRaw)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("\n[Raw SMC Key Dump]");
            Console.ResetColor();
            foreach (var kvp in SMCKeys.KnownSensors)
            {
                byte[]? raw = smc.ReadSMCKey(kvp.Key, out string dataType);
                if (raw != null)
                {
                    string hex = BitConverter.ToString(raw);
                    Console.WriteLine($"Key: {kvp.Key} | Type: {dataType,-5} | Hex: {hex,-12} | Sensor: {kvp.Value.Name}");
                }
            }
        }
    }

    private static void RunLiveWatchLoop(SMCService smc)
    {
        Console.WriteLine("\nEntering Live Watch Mode (Press Ctrl+C or 'Q' to quit)...\n");
        while (true)
        {
            if (Console.KeyAvailable && Console.ReadKey(true).Key == ConsoleKey.Q)
                break;

            Console.Clear();
            PrintHeader();
            RunDiagnosticScan(smc, false);
            Console.WriteLine($"\nUpdated at: {DateTime.Now:HH:mm:ss}. Refreshing in 1s (Press 'Q' to stop)...");
            Thread.Sleep(1000);
        }
    }

    private static void PrintDriverTroubleshootingGuide()
    {
        Console.ForegroundColor = ConsoleColor.DarkYellow;
        Console.WriteLine("\n--- SMC DRIVER SETUP INSTRUCTIONS ---");
        Console.WriteLine("To communicate with Apple SMC under Windows x64, WinRing0 must be installed:");
        Console.WriteLine("1. Ensure 'WinRing0x64.dll' and 'WinRing0x64.sys' are in the same folder as this .exe.");
        Console.WriteLine("2. Run Command Prompt / PowerShell as Administrator (Elevated).");
        Console.WriteLine("3. If Windows Defender flags WinRing0, allow it or add an exclusion for this tool folder.");
        Console.WriteLine("--------------------------------------\n");
        Console.ResetColor();
    }

    private static bool HasArg(string[] args, string flag)
    {
        foreach (var a in args)
        {
            if (string.Equals(a, flag, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static string? GetArgValue(string[] args, string flag)
    {
        for (int i = 0; i < args.Length; i++)
        {
            if (string.Equals(args[i], flag, StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                return args[i + 1];
            }
        }
        return null;
    }

    private static bool GetSetFanArgs(string[] args, out int fanIndex, out int targetRpm)
    {
        fanIndex = -1;
        targetRpm = -1;
        for (int i = 0; i < args.Length; i++)
        {
            if (string.Equals(args[i], "--set-fan", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 2 < args.Length && int.TryParse(args[i + 1], out fanIndex) && int.TryParse(args[i + 2], out targetRpm))
                {
                    return true;
                }
            }
        }
        return false;
    }
}
