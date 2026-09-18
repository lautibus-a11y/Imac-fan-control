using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace iMacFanControl.Diagnostic.Tray;

public static class AutoStartManager
{
    private const string TaskName = "iMacFanControl";

    public static bool IsSupported => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    public static bool IsAutoStartConfigured()
    {
        if (!IsSupported) return false;

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "schtasks.exe",
                Arguments = $"/query /tn \"{TaskName}\"",
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using var process = Process.Start(psi);
            if (process == null) return false;
            process.WaitForExit(2000);
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    public static bool EnableAutoStart(out string message)
    {
        if (!IsSupported)
        {
            message = "Task Scheduler autostart is only supported on Windows.";
            return false;
        }

        try
        {
            string exePath = Process.GetCurrentProcess().MainModule?.FileName 
                ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "iMacFanControl.Diagnostic.exe");

            // Build schtasks command: Run at user logon with highest privileges in background tray mode
            string commandArgs = $"/create /tn \"{TaskName}\" /tr \"\\\"{exePath}\\\" --tray\" /sc onlogon /rl highest /f";

            var psi = new ProcessStartInfo
            {
                FileName = "schtasks.exe",
                Arguments = commandArgs,
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using var process = Process.Start(psi);
            if (process == null)
            {
                message = "Failed to launch schtasks.exe.";
                return false;
            }

            string stdout = process.StandardOutput.ReadToEnd();
            string stderr = process.StandardError.ReadToEnd();
            process.WaitForExit(4000);

            if (process.ExitCode == 0)
            {
                message = "Auto-start task successfully registered in Windows Task Scheduler (runs on logon with highest privileges).";
                return true;
            }

            message = $"schtasks failed (code {process.ExitCode}): {stderr} {stdout}";
            return false;
        }
        catch (Exception ex)
        {
            message = $"Error creating auto-start task: {ex.Message}";
            return false;
        }
    }

    public static bool DisableAutoStart(out string message)
    {
        if (!IsSupported)
        {
            message = "Task Scheduler autostart is only supported on Windows.";
            return false;
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "schtasks.exe",
                Arguments = $"/delete /tn \"{TaskName}\" /f",
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using var process = Process.Start(psi);
            if (process == null)
            {
                message = "Failed to launch schtasks.exe.";
                return false;
            }

            process.WaitForExit(3000);
            message = "Auto-start task removed from Windows Task Scheduler.";
            return true;
        }
        catch (Exception ex)
        {
            message = $"Error removing auto-start task: {ex.Message}";
            return false;
        }
    }
}
