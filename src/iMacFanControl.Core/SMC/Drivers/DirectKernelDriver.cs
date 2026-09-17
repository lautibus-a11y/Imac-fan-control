using System;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace iMacFanControl.Core.SMC.Drivers;

/// <summary>
/// Direct Kernel Driver interface that communicates directly with WinRing0x64.sys
/// via Windows Service Control Manager and DeviceIoControl, eliminating dependency on WinRing0x64.dll.
/// </summary>
public class DirectKernelDriver : ISMCLowLevelDriver
{
    public string DriverName => "Direct WinRing0 Kernel Driver (WinRing0x64.sys)";
    public bool IsLoaded => _deviceHandle != null && !_deviceHandle.IsInvalid;

    private const string DriverServiceName = "WinRing0_1_2_0";
    private const string DevicePath = @"\\.\" + DriverServiceName;

    // IOCTL codes for WinRing0
    private const uint IOCTL_OLS_READ_IO_PORT_BYTE = 0x9C4060CC;
    private const uint IOCTL_OLS_WRITE_IO_PORT_BYTE = 0x9C40A0D8;

    private SafeFileHandle? _deviceHandle;

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct WriteIoPortInput
    {
        public uint PortNumber;
        public byte Value;
    }

    #region Win32 P/Invoke

    private const uint GENERIC_READ = 0x80000000;
    private const uint GENERIC_WRITE = 0x40000000;
    private const uint OPEN_EXISTING = 3;
    private const uint FILE_ATTRIBUTE_NORMAL = 0x80;

    private const uint SC_MANAGER_ALL_ACCESS = 0xF003F;
    private const uint SERVICE_ALL_ACCESS = 0xF01FF;
    private const uint SERVICE_KERNEL_DRIVER = 0x00000001;
    private const uint SERVICE_DEMAND_START = 0x00000003;
    private const uint SERVICE_ERROR_NORMAL = 0x00000001;
    private const uint SERVICE_CONTROL_STOP = 0x00000001;

    private const int ERROR_SERVICE_EXISTS = 1073;
    private const int ERROR_SERVICE_ALREADY_RUNNING = 1056;

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern SafeFileHandle CreateFile(
        string lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        IntPtr lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(
        SafeFileHandle hDevice,
        uint dwIoControlCode,
        ref uint lpInBuffer,
        uint nInBufferSize,
        out uint lpOutBuffer,
        uint nOutBufferSize,
        out uint lpBytesReturned,
        IntPtr lpOverlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(
        SafeFileHandle hDevice,
        uint dwIoControlCode,
        ref WriteIoPortInput lpInBuffer,
        uint nInBufferSize,
        IntPtr lpOutBuffer,
        uint nOutBufferSize,
        out uint lpBytesReturned,
        IntPtr lpOverlapped);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern IntPtr OpenSCManager(string? lpMachineName, string? lpDatabaseName, uint dwDesiredAccess);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern IntPtr CreateService(
        IntPtr hSCManager,
        string lpServiceName,
        string lpDisplayName,
        uint dwDesiredAccess,
        uint dwServiceType,
        uint dwStartType,
        uint dwErrorControl,
        string lpBinaryPathName,
        string? lpLoadOrderGroup,
        string? lpdwTagId,
        string? lpDependencies,
        string? lpServiceStartName,
        string? lpPassword);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern IntPtr OpenService(IntPtr hSCManager, string lpServiceName, uint dwDesiredAccess);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool StartService(IntPtr hService, uint dwNumServiceArgs, string[]? lpServiceArgVectors);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool ControlService(IntPtr hService, uint dwControl, IntPtr lpServiceStatus);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool DeleteService(IntPtr hService);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool CloseServiceHandle(IntPtr hSCObject);

    #endregion

    public bool Initialize(out string errorMessage)
    {
        errorMessage = string.Empty;

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            errorMessage = "DirectKernelDriver requires Windows x64.";
            return false;
        }

        // 1. Check if device is already open / running
        if (TryOpenDevice())
        {
            return true;
        }

        // 2. Locate WinRing0x64.sys
        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        string sysPath = Path.Combine(baseDir, "WinRing0x64.sys");

        if (!File.Exists(sysPath))
        {
            errorMessage = $"Driver file 'WinRing0x64.sys' not found in '{baseDir}'.";
            return false;
        }

        // 3. Register and start driver service via Service Control Manager
        if (!InstallAndStartService(sysPath, out string scmError))
        {
            errorMessage = $"SCM Driver error: {scmError}. Administrator privileges are required.";
            return false;
        }

        // 4. Open handle to driver device \\.\WinRing0_1_2_0
        if (!TryOpenDevice())
        {
            int err = Marshal.GetLastWin32Error();
            errorMessage = $"Failed to open device handle '{DevicePath}' (Win32 Error: {err}).";
            return false;
        }

        return true;
    }

    private bool TryOpenDevice()
    {
        _deviceHandle = CreateFile(
            DevicePath,
            GENERIC_READ | GENERIC_WRITE,
            0,
            IntPtr.Zero,
            OPEN_EXISTING,
            FILE_ATTRIBUTE_NORMAL,
            IntPtr.Zero);

        if (_deviceHandle.IsInvalid)
        {
            _deviceHandle.Dispose();
            _deviceHandle = null;
            return false;
        }

        return true;
    }

    private static bool InstallAndStartService(string sysFullPath, out string error)
    {
        error = string.Empty;
        IntPtr scm = OpenSCManager(null, null, SC_MANAGER_ALL_ACCESS);
        if (scm == IntPtr.Zero)
        {
            error = $"OpenSCManager failed with error {Marshal.GetLastWin32Error()}";
            return false;
        }

        try
        {
            // Check if service already exists
            IntPtr service = OpenService(scm, DriverServiceName, SERVICE_ALL_ACCESS);
            if (service == IntPtr.Zero)
            {
                // Create service
                service = CreateService(
                    scm,
                    DriverServiceName,
                    DriverServiceName,
                    SERVICE_ALL_ACCESS,
                    SERVICE_KERNEL_DRIVER,
                    SERVICE_DEMAND_START,
                    SERVICE_ERROR_NORMAL,
                    sysFullPath,
                    null, null, null, null, null);

                if (service == IntPtr.Zero)
                {
                    int err = Marshal.GetLastWin32Error();
                    if (err != ERROR_SERVICE_EXISTS)
                    {
                        error = $"CreateService failed with error {err}";
                        return false;
                    }
                    service = OpenService(scm, DriverServiceName, SERVICE_ALL_ACCESS);
                }
            }

            if (service != IntPtr.Zero)
            {
                // Start service
                if (!StartService(service, 0, null))
                {
                    int err = Marshal.GetLastWin32Error();
                    if (err != ERROR_SERVICE_ALREADY_RUNNING)
                    {
                        error = $"StartService failed with error {err}";
                        CloseServiceHandle(service);
                        return false;
                    }
                }
                CloseServiceHandle(service);
                return true;
            }

            error = "Unable to create or open service handle.";
            return false;
        }
        finally
        {
            CloseServiceHandle(scm);
        }
    }

    public byte ReadPort8(ushort port)
    {
        if (_deviceHandle == null || _deviceHandle.IsInvalid)
            throw new InvalidOperationException("Direct driver handle is invalid.");

        uint portInput = port;
        if (!DeviceIoControl(_deviceHandle, IOCTL_OLS_READ_IO_PORT_BYTE, ref portInput, sizeof(uint), out uint outputVal, sizeof(uint), out _, IntPtr.Zero))
        {
            return 0;
        }

        return (byte)(outputVal & 0xFF);
    }

    public void WritePort8(ushort port, byte value)
    {
        if (_deviceHandle == null || _deviceHandle.IsInvalid)
            throw new InvalidOperationException("Direct driver handle is invalid.");

        var input = new WriteIoPortInput
        {
            PortNumber = port,
            Value = value
        };

        DeviceIoControl(_deviceHandle, IOCTL_OLS_WRITE_IO_PORT_BYTE, ref input, (uint)Marshal.SizeOf(input), IntPtr.Zero, 0, out _, IntPtr.Zero);
    }

    public void Close()
    {
        if (_deviceHandle != null && !_deviceHandle.IsInvalid)
        {
            _deviceHandle.Close();
            _deviceHandle.Dispose();
            _deviceHandle = null;
        }
    }
}
