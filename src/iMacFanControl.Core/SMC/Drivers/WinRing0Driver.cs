using System;
using System.IO;
using System.Runtime.InteropServices;

namespace iMacFanControl.Core.SMC.Drivers;

public class WinRing0Driver : ISMCLowLevelDriver
{
    public string DriverName => "WinRing0 (OpenLibSys x64 Driver)";
    public bool IsLoaded { get; private set; }

    private const string DllName = "WinRing0x64.dll";

    // OLS DLL Status Codes
    public const uint OLS_DLL_NO_ERROR = 0;
    public const uint OLS_DLL_UNSUPPORTED_PLATFORM = 1;
    public const uint OLS_DLL_DRIVER_NOT_LOADED = 2;
    public const uint OLS_DLL_DRIVER_NOT_FOUND = 3;
    public const uint OLS_DLL_DRIVER_UNLOADED = 4;
    public const uint OLS_DLL_DRIVER_NOT_LOADED_ON_NETWORK = 5;

    [DllImport(DllName, EntryPoint = "InitializeOls", CallingConvention = CallingConvention.StdCall)]
    private static extern int InitializeOls();

    [DllImport(DllName, EntryPoint = "DeinitializeOls", CallingConvention = CallingConvention.StdCall)]
    private static extern void DeinitializeOls();

    [DllImport(DllName, EntryPoint = "GetDllStatus", CallingConvention = CallingConvention.StdCall)]
    private static extern uint GetDllStatus();

    [DllImport(DllName, EntryPoint = "GetDriverStatus", CallingConvention = CallingConvention.StdCall)]
    private static extern uint GetDriverStatus();

    [DllImport(DllName, EntryPoint = "ReadIoPortByte", CallingConvention = CallingConvention.StdCall)]
    private static extern byte NativeReadIoPortByte(ushort port);

    [DllImport(DllName, EntryPoint = "WriteIoPortByte", CallingConvention = CallingConvention.StdCall)]
    private static extern void NativeWriteIoPortByte(ushort port, byte value);

    public bool Initialize(out string errorMessage)
    {
        errorMessage = string.Empty;

        // Verify operating system
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            errorMessage = "WinRing0 requires Microsoft Windows.";
            IsLoaded = false;
            return false;
        }

        // Check if DLL exists in current application directory or system path
        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        string dllPath = Path.Combine(baseDir, DllName);
        string sysPath = Path.Combine(baseDir, "WinRing0x64.sys");

        if (!File.Exists(dllPath))
        {
            errorMessage = $"Missing '{DllName}' in '{baseDir}'. " +
                           "Please ensure WinRing0x64.dll and WinRing0x64.sys are present in the application directory.";
            IsLoaded = false;
            return false;
        }

        try
        {
            int initResult = InitializeOls();
            uint status = GetDllStatus();

            if (initResult == 0 || status != OLS_DLL_NO_ERROR)
            {
                uint driverStatus = GetDriverStatus();
                string reason = status switch
                {
                    OLS_DLL_UNSUPPORTED_PLATFORM => "Unsupported operating system or processor architecture.",
                    OLS_DLL_DRIVER_NOT_LOADED => "Driver could not be loaded. Please run the application as Administrator.",
                    OLS_DLL_DRIVER_NOT_FOUND => $"Driver file '{sysPath}' was not found. Please place WinRing0x64.sys in the app folder.",
                    OLS_DLL_DRIVER_UNLOADED => "Driver was unloaded by the system.",
                    OLS_DLL_DRIVER_NOT_LOADED_ON_NETWORK => "Cannot load driver from a network drive.",
                    _ => $"Unknown error (DLL status code: {status}, Driver status code: {driverStatus}). Administrator rights are required."
                };

                errorMessage = $"WinRing0 initialization failed: {reason}";
                IsLoaded = false;
                return false;
            }

            IsLoaded = true;
            return true;
        }
        catch (DllNotFoundException)
        {
            errorMessage = $"Could not load '{DllName}'. Ensure it is located in '{baseDir}' and is a 64-bit DLL.";
            IsLoaded = false;
            return false;
        }
        catch (BadImageFormatException ex)
        {
            errorMessage = $"Architecture mismatch when loading '{DllName}': {ex.Message}. Make sure the build targets x64.";
            IsLoaded = false;
            return false;
        }
        catch (Exception ex)
        {
            errorMessage = $"Unexpected error initializing WinRing0: {ex.Message}";
            IsLoaded = false;
            return false;
        }
    }

    public byte ReadPort8(ushort port)
    {
        if (!IsLoaded)
            throw new InvalidOperationException("WinRing0 driver is not initialized or failed to load.");

        return NativeReadIoPortByte(port);
    }

    public void WritePort8(ushort port, byte value)
    {
        if (!IsLoaded)
            throw new InvalidOperationException("WinRing0 driver is not initialized or failed to load.");

        NativeWriteIoPortByte(port, value);
    }

    public void Close()
    {
        if (IsLoaded)
        {
            try
            {
                DeinitializeOls();
            }
            catch
            {
                // Suppress on shutdown
            }
            finally
            {
                IsLoaded = false;
            }
        }
    }
}
