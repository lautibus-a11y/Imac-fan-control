using System;
using System.IO;
using System.Runtime.InteropServices;

namespace iMacFanControl.Core.SMC.Drivers;

public class InpOutDriver : ISMCLowLevelDriver
{
    public string DriverName => "InpOut32 / InpOutx64 Direct I/O Driver";
    public bool IsLoaded { get; private set; }

    private const string DllName = "inpoutx64.dll";

    [DllImport(DllName, EntryPoint = "IsInpOutDriverOpen")]
    private static extern int IsInpOutDriverOpen();

    [DllImport(DllName, EntryPoint = "DlPortReadPortUchar")]
    private static extern byte DlPortReadPortUchar(ushort port);

    [DllImport(DllName, EntryPoint = "DlPortWritePortUchar")]
    private static extern void DlPortWritePortUchar(ushort port, byte value);

    public bool Initialize(out string errorMessage)
    {
        errorMessage = string.Empty;

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            errorMessage = "InpOutx64 requires Microsoft Windows.";
            IsLoaded = false;
            return false;
        }

        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        string dllPath = Path.Combine(baseDir, DllName);

        if (!File.Exists(dllPath))
        {
            errorMessage = $"'{DllName}' not found in '{baseDir}'.";
            IsLoaded = false;
            return false;
        }

        try
        {
            int isOpen = IsInpOutDriverOpen();
            if (isOpen == 0)
            {
                errorMessage = "InpOut driver could not be opened. Ensure the application runs with Administrator privileges.";
                IsLoaded = false;
                return false;
            }

            IsLoaded = true;
            return true;
        }
        catch (Exception ex)
        {
            errorMessage = $"InpOut initialization failed: {ex.Message}";
            IsLoaded = false;
            return false;
        }
    }

    public byte ReadPort8(ushort port)
    {
        if (!IsLoaded)
            throw new InvalidOperationException("InpOut driver is not loaded.");
        return DlPortReadPortUchar(port);
    }

    public void WritePort8(ushort port, byte value)
    {
        if (!IsLoaded)
            throw new InvalidOperationException("InpOut driver is not loaded.");
        DlPortWritePortUchar(port, value);
    }

    public void Close()
    {
        IsLoaded = false;
    }
}
