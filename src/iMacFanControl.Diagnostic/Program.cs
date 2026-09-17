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
        Console.Title = "iMac Fan Control - Hardware Diagnostic (Phase 1)";

        PrintHeader();

        bool useMock = HasArg(args, "--mock");
        bool watchMode = HasArg(args, "--watch") || HasArg(args, "-w");
        bool dumpKeys = HasArg(args, "--dump");

        // 1. Operating System & Admin Privilege Check
        CheckPlatformAndPrivileges(useMock);

        // 2. Detect Machine Hardware
        var compatService = new HardwareCompatibilityService();
        var machineInfo = compatService.DetectMachine();
        PrintMachineInfo(machineInfo);

        // 3. Select and Initialize SMC Driver
        ISMCLowLevelDriver driver = SelectDriver(useMock);
        var smcService = new SMCService(driver);

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
            Console.ForegroundColor = ConsoleColor.DarkCyan;
            Console.WriteLine("Tip: Run with '--watch' for live updating view, or '--dump' for raw SMC key dump.");
            Console.ResetColor();
            Console.WriteLine("Phase 1 Diagnostic Complete. SMC writing remains locked.");
            Console.WriteLine("Press any key to exit...");
            try { Console.ReadKey(); } catch { }
        }

        smcService.Close();
    }

    private static void PrintHeader()
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine(@"===============================================================================");
        Console.WriteLine(@"                  iMac Fan Control - Hardware Diagnostic                      ");
        Console.WriteLine(@"                 Phase 1: SMC Hardware Read & Verification                    ");
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
}
