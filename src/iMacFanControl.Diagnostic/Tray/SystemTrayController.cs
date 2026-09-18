using System;
using System.Runtime.InteropServices;
using System.Threading;
using iMacFanControl.Core.Models;
using iMacFanControl.Core.Services;
using iMacFanControl.Core.SMC;

namespace iMacFanControl.Diagnostic.Tray;

public class SystemTrayController : IDisposable
{
    private const int SW_HIDE = 0;
    private const int SW_SHOW = 5;

    private const uint WM_USER = 0x0400;
    private const uint WM_TRAY_CALLBACK = WM_USER + 101;
    private const uint WM_RBUTTONUP = 0x0205;
    private const uint WM_LBUTTONDBLCLK = 0x0203;
    private const uint WM_TIMER = 0x0113;
    private const uint WM_DESTROY = 0x0002;

    private const uint NIM_ADD = 0x00000000;
    private const uint NIM_MODIFY = 0x00000001;
    private const uint NIM_DELETE = 0x00000002;
    private const uint NIF_MESSAGE = 0x00000001;
    private const uint NIF_ICON = 0x00000002;
    private const uint NIF_TIP = 0x00000004;
    private const uint NIF_INFO = 0x00000010;

    private const uint MF_STRING = 0x00000000;
    private const uint MF_SEPARATOR = 0x00000800;
    private const uint MF_GRAYED = 0x00000001;
    private const uint MF_CHECKED = 0x00000008;
    private const uint TPM_RETURNCMD = 0x0100;
    private const uint TPM_RIGHTBUTTON = 0x0002;

    private const int CMD_HEADER = 1001;
    private const int CMD_STATUS = 1002;
    private const int CMD_PROFILE_NORMAL = 1003;
    private const int CMD_PROFILE_SILENT = 1004;
    private const int CMD_PROFILE_GAMING = 1005;
    private const int CMD_RESTORE_AUTO = 1006;
    private const int CMD_TOGGLE_AUTOSTART = 1007;
    private const int CMD_TOGGLE_CONSOLE = 1008;
    private const int CMD_EXIT = 1009;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATA
    {
        public int cbSize;
        public IntPtr hWnd;
        public int uID;
        public int uFlags;
        public int uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
        public int dwState;
        public int dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;
        public int uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;
        public int dwInfoFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public POINT pt;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSEX
    {
        public int cbSize;
        public int style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
        public IntPtr hIconSm;
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetConsoleWindow();

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool AppendMenu(IntPtr hMenu, uint uFlags, IntPtr uIDNewItem, string lpNewItem);

    [DllImport("user32.dll")]
    private static extern int TrackPopupMenu(IntPtr hMenu, uint uFlags, int x, int y, int nReserved, IntPtr hWnd, IntPtr prcRect);

    [DllImport("user32.dll")]
    private static extern bool DestroyMenu(IntPtr hMenu);

    [DllImport("user32.dll")]
    private static extern IntPtr LoadIcon(IntPtr hInstance, IntPtr lpIconName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern ushort RegisterClassEx(ref WNDCLASSEX lpwcx);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowEx(
        int dwExStyle, string lpClassName, string lpWindowName,
        int dwStyle, int x, int y, int nWidth, int nHeight,
        IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    private static extern void PostQuitMessage(int nExitCode);

    [DllImport("user32.dll")]
    private static extern UIntPtr SetTimer(IntPtr hWnd, UIntPtr nIDEvent, uint uElapse, IntPtr lpTimerFunc);

    [DllImport("user32.dll")]
    private static extern bool KillTimer(IntPtr hWnd, UIntPtr uIDEvent);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIcon(uint dwMessage, ref NOTIFYICONDATA lpData);

    private readonly SMCService _smcService;
    private readonly FanControlService _fanControlService;
    private readonly ProfileService _profileService;
    private readonly SafetyService _safetyService;

    private IntPtr _hwnd = IntPtr.Zero;
    private IntPtr _consoleHwnd = IntPtr.Zero;
    private bool _isConsoleVisible = false;
    private WndProcDelegate? _wndProcDelegate;
    private NOTIFYICONDATA _nid;
    private bool _nidCreated = false;
    private string _currentStatus = "Monitoreando hardware...";
    private string _activeProfileName = "Normal (Auto Apple)";

    public SystemTrayController(
        SMCService smcService,
        FanControlService fanControlService,
        ProfileService profileService,
        SafetyService safetyService)
    {
        _smcService = smcService;
        _fanControlService = fanControlService;
        _profileService = profileService;
        _safetyService = safetyService;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            _consoleHwnd = GetConsoleWindow();
        }
    }

    public void Run()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            RunNonWindowsFallback();
            return;
        }

        // Hide console window in background tray mode
        HideConsole();

        // Unlock writing
        _smcService.UnlockHardwareWriting(SMCWriter.ConfirmationToken);

        // Register window class
        string className = "iMacFanControlTrayClass_" + Guid.NewGuid().ToString("N");
        IntPtr hInstance = GetModuleHandle(null);

        _wndProcDelegate = WndProc;
        var wcx = new WNDCLASSEX
        {
            cbSize = Marshal.SizeOf<WNDCLASSEX>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProcDelegate),
            hInstance = hInstance,
            lpszClassName = className
        };

        RegisterClassEx(ref wcx);

        // Create message-only / hidden window
        _hwnd = CreateWindowEx(0, className, "iMacFanControlTray", 0, 0, 0, 0, 0, IntPtr.Zero, IntPtr.Zero, hInstance, IntPtr.Zero);
        if (_hwnd == IntPtr.Zero)
        {
            ShowConsole();
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("Error: Unable to create Win32 message window for system tray.");
            Console.ResetColor();
            return;
        }

        // Add NotifyIcon
        IntPtr hIcon = LoadIcon(IntPtr.Zero, (IntPtr)32512); // IDI_APPLICATION
        _nid = new NOTIFYICONDATA
        {
            cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd = _hwnd,
            uID = 1,
            uFlags = (int)(NIF_MESSAGE | NIF_ICON | NIF_TIP | NIF_INFO),
            uCallbackMessage = (int)WM_TRAY_CALLBACK,
            hIcon = hIcon,
            szTip = "iMac Fan Control",
            szInfoTitle = "iMac Fan Control Activo",
            szInfo = "Controlando ventiladores y monitoreando en segundo plano.",
            dwInfoFlags = 1 // NIIF_INFO
        };

        _nidCreated = Shell_NotifyIcon(NIM_ADD, ref _nid);

        // Start 2-second update timer
        SetTimer(_hwnd, (UIntPtr)1, 2000, IntPtr.Zero);
        UpdateStatus();

        // Message pump
        while (GetMessage(out MSG msg, IntPtr.Zero, 0, 0) > 0)
        {
            TranslateMessage(ref msg);
            DispatchMessage(ref msg);
        }
    }

    private void RunNonWindowsFallback()
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("\n[Simulación Tray] El icono de bandeja nativo de Windows (NotifyIcon) requiere Windows.");
        Console.WriteLine("Ejecutando bucle de segundo plano simulado (Presiona Ctrl+C para detener)...\n");
        Console.ResetColor();

        while (true)
        {
            var fans = _smcService.GetFans();
            float? cpu = _smcService.GetSensorTemperature("TC0D");
            float? gpu = _smcService.GetSensorTemperature("TG0D");
            float? hdd = _smcService.GetSensorTemperature("TH0P");

            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] CPU: {cpu:F1}°C | GPU: {gpu:F1}°C | HDD: {hdd:F1}°C | Ventiladores activos: {fans.Count}");
            Thread.Sleep(3000);
        }
    }

    private IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WM_TRAY_CALLBACK)
        {
            uint action = (uint)lParam.ToInt64();
            if (action == WM_RBUTTONUP)
            {
                ShowContextMenu();
                return IntPtr.Zero;
            }
            else if (action == WM_LBUTTONDBLCLK)
            {
                ToggleConsole();
                return IntPtr.Zero;
            }
        }
        else if (msg == WM_TIMER)
        {
            UpdateStatus();
            return IntPtr.Zero;
        }
        else if (msg == WM_DESTROY)
        {
            PostQuitMessage(0);
            return IntPtr.Zero;
        }

        return DefWindowProc(hWnd, msg, wParam, lParam);
    }

    private void ShowContextMenu()
    {
        IntPtr hMenu = CreatePopupMenu();
        if (hMenu == IntPtr.Zero) return;

        bool isAutostart = AutoStartManager.IsAutoStartConfigured();

        // Menu items
        AppendMenu(hMenu, MF_STRING | MF_GRAYED, (IntPtr)CMD_HEADER, "iMac Fan Control");
        AppendMenu(hMenu, MF_STRING | MF_GRAYED, (IntPtr)CMD_STATUS, $"  {_currentStatus}");
        AppendMenu(hMenu, MF_SEPARATOR, IntPtr.Zero, string.Empty);

        AppendMenu(hMenu, MF_STRING, (IntPtr)CMD_PROFILE_NORMAL, $"Perfil: Normal (Auto Apple) {(_activeProfileName.Contains("Normal") ? "✔" : "")}");
        AppendMenu(hMenu, MF_STRING, (IntPtr)CMD_PROFILE_SILENT, $"Perfil: Silencioso {(_activeProfileName.Contains("Silencioso") ? "✔" : "")}");
        AppendMenu(hMenu, MF_STRING, (IntPtr)CMD_PROFILE_GAMING, $"Perfil: Gaming / Rendimiento {(_activeProfileName.Contains("Gaming") ? "✔" : "")}");
        AppendMenu(hMenu, MF_SEPARATOR, IntPtr.Zero, string.Empty);

        AppendMenu(hMenu, MF_STRING, (IntPtr)CMD_RESTORE_AUTO, "Restaurar todo a Automático (SMC)");
        AppendMenu(hMenu, MF_STRING | (isAutostart ? MF_CHECKED : 0), (IntPtr)CMD_TOGGLE_AUTOSTART, "Iniciar con Windows (Admin)");
        AppendMenu(hMenu, MF_SEPARATOR, IntPtr.Zero, string.Empty);

        AppendMenu(hMenu, MF_STRING, (IntPtr)CMD_TOGGLE_CONSOLE, _isConsoleVisible ? "Ocultar Consola" : "Mostrar Consola");
        AppendMenu(hMenu, MF_STRING, (IntPtr)CMD_EXIT, "Salir");

        GetCursorPos(out POINT pt);
        SetForegroundWindow(_hwnd);

        int cmd = TrackPopupMenu(hMenu, TPM_RETURNCMD | TPM_RIGHTBUTTON, pt.X, pt.Y, 0, _hwnd, IntPtr.Zero);
        DestroyMenu(hMenu);

        HandleMenuCommand(cmd);
    }

    private void HandleMenuCommand(int cmd)
    {
        switch (cmd)
        {
            case CMD_PROFILE_NORMAL:
                _fanControlService.RestoreAllToAuto();
                _activeProfileName = "Normal (Auto Apple)";
                UpdateStatus();
                break;
            case CMD_PROFILE_SILENT:
                ApplyProfile("silent", "Silencioso");
                break;
            case CMD_PROFILE_GAMING:
                ApplyProfile("gaming", "Gaming");
                break;
            case CMD_RESTORE_AUTO:
                _fanControlService.RestoreAllToAuto();
                _activeProfileName = "Normal (Auto Apple)";
                UpdateStatus();
                break;
            case CMD_TOGGLE_AUTOSTART:
                ToggleAutoStart();
                break;
            case CMD_TOGGLE_CONSOLE:
                ToggleConsole();
                break;
            case CMD_EXIT:
                Exit();
                break;
        }
    }

    private void ApplyProfile(string profileId, string displayName)
    {
        var fans = _smcService.GetFans();
        var profiles = _profileService.GetBuiltInProfiles(fans);
        var target = profiles.Find(p => p.Id.Contains(profileId, StringComparison.OrdinalIgnoreCase));

        if (target != null)
        {
            foreach (var fan in fans)
            {
                if (target.FanModes.TryGetValue(fan.Index, out var mode))
                {
                    if (mode == FanControlMode.Auto)
                        _fanControlService.RestoreFanToAuto(fan.Index);
                    else if (target.ManualRpms.TryGetValue(fan.Index, out int rpm))
                        _fanControlService.SetFanManualRpm(fan.Index, rpm);
                }
            }
            _activeProfileName = displayName;
            UpdateStatus();
        }
    }

    private void ToggleAutoStart()
    {
        bool current = AutoStartManager.IsAutoStartConfigured();
        if (current)
        {
            AutoStartManager.DisableAutoStart(out _);
        }
        else
        {
            AutoStartManager.EnableAutoStart(out _);
        }
    }

    private void UpdateStatus()
    {
        try
        {
            float? cpuTemp = _smcService.GetSensorTemperature("TC0D") ?? _smcService.GetSensorTemperature("TC0P");
            float? gpuTemp = _smcService.GetSensorTemperature("TG0D");
            float? hddTemp = _smcService.GetSensorTemperature("TH0P");

            var fans = _smcService.GetFans();
            int hddRpm = 0;
            foreach (var f in fans)
            {
                if (f.Name.Contains("HDD", StringComparison.OrdinalIgnoreCase))
                    hddRpm = f.CurrentRpm;
            }

            _currentStatus = $"CPU: {cpuTemp?.ToString("F0") ?? "--"}°C | GPU: {gpuTemp?.ToString("F0") ?? "--"}°C | HDD: {hddTemp?.ToString("F0") ?? "--"}°C";

            string tip = $"iMac Fan: CPU {cpuTemp:F0}°C | HDD {hddTemp:F0}°C ({hddRpm} RPM) - {_activeProfileName}";
            if (tip.Length > 127) tip = tip.Substring(0, 127);

            _nid.szTip = tip;
            _nid.uFlags = (int)NIF_TIP;
            Shell_NotifyIcon(NIM_MODIFY, ref _nid);
        }
        catch
        {
            // Ignore polling errors
        }
    }

    private void ToggleConsole()
    {
        if (_consoleHwnd == IntPtr.Zero)
            _consoleHwnd = GetConsoleWindow();

        if (_consoleHwnd != IntPtr.Zero)
        {
            if (_isConsoleVisible)
                HideConsole();
            else
                ShowConsole();
        }
    }

    private void ShowConsole()
    {
        if (_consoleHwnd != IntPtr.Zero)
        {
            ShowWindow(_consoleHwnd, SW_SHOW);
            _isConsoleVisible = true;
        }
    }

    private void HideConsole()
    {
        if (_consoleHwnd != IntPtr.Zero)
        {
            ShowWindow(_consoleHwnd, SW_HIDE);
            _isConsoleVisible = false;
        }
    }

    public void Exit()
    {
        try
        {
            // Safety first: restore all fans to Apple SMC auto mode
            _fanControlService.RestoreAllToAuto();

            if (_nidCreated)
            {
                Shell_NotifyIcon(NIM_DELETE, ref _nid);
                _nidCreated = false;
            }

            if (_hwnd != IntPtr.Zero)
            {
                KillTimer(_hwnd, (UIntPtr)1);
                DestroyWindow(_hwnd);
                _hwnd = IntPtr.Zero;
            }

            ShowConsole();
        }
        finally
        {
            Environment.Exit(0);
        }
    }

    public void Dispose()
    {
        Exit();
    }
}
