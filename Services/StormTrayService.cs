using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace StormKeys.Services;

/// <summary>
/// Нативный сервис системного трея со 100% надёжностью, тёмным оформлением MIDNIGHT (#141026),
/// выделенным окном сообщений, поддержкой TaskbarCreated (перезапуск Explorer)
/// и гарантированным закрытием процесса по стандарту экосистемы STORM.
/// </summary>
public sealed class StormTrayService : IDisposable
{
    private const int WM_USER = 0x0400;
    private const int WM_TRAYICON = WM_USER + 2026;
    private const int WM_LBUTTONUP = 0x0202;
    private const int WM_LBUTTONDBLCLK = 0x0203;
    private const int WM_RBUTTONUP = 0x0205;
    private const int WM_CONTEXTMENU = 0x007B;
    private const int WM_DESTROY = 0x0002;

    private const int NIM_ADD = 0x00000000;
    private const int NIM_MODIFY = 0x00000001;
    private const int NIM_DELETE = 0x00000002;
    private const int NIM_SETVERSION = 0x00000004;

    private const int NIF_MESSAGE = 0x00000001;
    private const int NIF_ICON = 0x00000002;
    private const int NIF_TIP = 0x00000004;
    private const int NIF_INFO = 0x00000010;

    private const int NIIF_INFO = 0x00000001;
    private const int NOTIFYICON_VERSION_4 = 4;

    private const int MF_STRING = 0x00000000;
    private const int MF_SEPARATOR = 0x00000800;
    private const int TPM_RIGHTBUTTON = 0x0002;
    private const int TPM_RETURNCMD = 0x0100;

    private const uint MIM_BACKGROUND = 0x00000002;
    private const uint MIM_APPLYTOSUBMENUS = 0x80000000;

    private const int CMD_RESTORE = 1001;
    private const int CMD_ABOUT = 1002;
    private const int CMD_EXIT = 1003;

    private const uint WS_POPUP = 0x80000000;
    private const uint WS_EX_TOOLWINDOW = 0x00000080;

    private const int SM_CXSMICON = 49;
    private const int SM_CYSMICON = 50;

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
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MENUINFO
    {
        public uint cbSize;
        public uint fMask;
        public uint dwStyle;
        public uint cyMax;
        public IntPtr hbrBack;
        public uint dwContextHelpID;
        public UIntPtr dwMenuData;
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSEXW
    {
        public uint cbSize;
        public uint style;
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

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIconW(int dwMessage, ref NOTIFYICONDATA lpdata);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern uint ExtractIconExW(string lpszFile, int nIconIndex, out IntPtr phiconLarge, out IntPtr phiconSmall, uint nIcons);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool AppendMenuW(IntPtr hMenu, int uFlags, IntPtr uIDNewItem, string lpNewItem);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetMenuDefaultItem(IntPtr hMenu, uint uItem, uint fByPos);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetMenuInfo(IntPtr hMenu, ref MENUINFO lpcmi);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr CreateSolidBrush(uint crColor);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern bool DeleteObject(IntPtr hObject);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int TrackPopupMenuEx(IntPtr hmenu, uint fuFlags, int x, int y, IntPtr hwnd, IntPtr lptpm);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyMenu(IntPtr hMenu);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool PostMessageW(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern ushort RegisterClassExW(ref WNDCLASSEXW lpwcx);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowExW(
        uint dwExStyle, string lpClassName, string lpWindowName, uint dwStyle,
        int x, int y, int nWidth, int nHeight,
        IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProcW(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessageW(string lpString);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern IntPtr LoadImage(IntPtr hinst, string lpszName, uint uType, int cxDesired, int cyDesired, uint fuLoad);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    private readonly WndProcDelegate _wndProcDelegate;
    private readonly uint _wmTaskbarCreated;
    private IntPtr _hTrayWnd = IntPtr.Zero;
    private IntPtr _hIcon = IntPtr.Zero;
    private NOTIFYICONDATA _nid;
    private bool _isCreated;
    private bool _isDisposed;

    public Action? OnRestoreRequested { get; set; }
    public Action? OnAboutRequested { get; set; }
    public Action? OnExitRequested { get; set; }

    public StormTrayService(IntPtr ownerHWnd)
    {
        _wndProcDelegate = TrayWindowProc;
        _wmTaskbarCreated = RegisterWindowMessageW("TaskbarCreated");

        CreateTrayHelperWindow();
        InitializeTrayIcon();
    }

    private void CreateTrayHelperWindow()
    {
        try
        {
            string className = $"STORM_KEYS_TRAY_{Guid.NewGuid():N}";
            var wcx = new WNDCLASSEXW
            {
                cbSize = (uint)Marshal.SizeOf<WNDCLASSEXW>(),
                style = 0,
                lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProcDelegate),
                cbClsExtra = 0,
                cbWndExtra = 0,
                hInstance = Process.GetCurrentProcess().Handle,
                hIcon = IntPtr.Zero,
                hCursor = IntPtr.Zero,
                hbrBackground = IntPtr.Zero,
                lpszMenuName = null,
                lpszClassName = className,
                hIconSm = IntPtr.Zero
            };

            ushort atom = RegisterClassExW(ref wcx);
            if (atom != 0)
            {
                _hTrayWnd = CreateWindowExW(
                    WS_EX_TOOLWINDOW,
                    className,
                    "STORM_KEYS_TRAY_WINDOW",
                    WS_POPUP,
                    -32000, -32000, 1, 1,
                    IntPtr.Zero, IntPtr.Zero, Process.GetCurrentProcess().Handle, IntPtr.Zero);
            }
        }
        catch { }
    }

    private IntPtr LoadTrayIconHandle()
    {
        int cx = GetSystemMetrics(SM_CXSMICON);
        int cy = GetSystemMetrics(SM_CYSMICON);
        if (cx <= 0) cx = 16;
        if (cy <= 0) cy = 16;

        // 1. Попытка загрузить из Assets\AppIcon.ico
        string[] candidates = {
            Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico"),
            Path.Combine(AppContext.BaseDirectory, "app.ico"),
            @"F:\STORM KEYS\Assets\AppIcon.ico"
        };

        foreach (var path in candidates)
        {
            if (File.Exists(path))
            {
                try
                {
                    IntPtr h = LoadImage(IntPtr.Zero, path, 1 /*IMAGE_ICON*/, cx, cy, 0x00000010 /*LR_LOADFROMFILE*/);
                    if (h != IntPtr.Zero) return h;
                }
                catch { }
            }
        }

        // 2. Извлечение маленького значка из текущего .exe
        try
        {
            string? exe = Process.GetCurrentProcess().MainModule?.FileName;
            if (!string.IsNullOrEmpty(exe) && File.Exists(exe))
            {
                if (ExtractIconExW(exe, 0, out _, out IntPtr hSmall, 1) > 0 && hSmall != IntPtr.Zero)
                    return hSmall;
            }
        }
        catch { }

        return IntPtr.Zero;
    }

    private void InitializeTrayIcon()
    {
        if (_hTrayWnd == IntPtr.Zero) return;

        _hIcon = LoadTrayIconHandle();

        _nid = new NOTIFYICONDATA
        {
            cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd = _hTrayWnd,
            uID = 1001,
            uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP,
            uCallbackMessage = WM_TRAYICON,
            hIcon = _hIcon,
            szTip = "STORM KEYS"
        };

        _isCreated = Shell_NotifyIconW(NIM_ADD, ref _nid);
        if (_isCreated)
        {
            _nid.uTimeoutOrVersion = NOTIFYICON_VERSION_4;
            Shell_NotifyIconW(NIM_SETVERSION, ref _nid);
        }
    }

    private IntPtr TrayWindowProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam)
    {
        if (_wmTaskbarCreated != 0 && uMsg == _wmTaskbarCreated)
        {
            // Перезапуск Windows Explorer / смена темы: пересоздаём иконку в трее
            if (_hIcon == IntPtr.Zero)
                _hIcon = LoadTrayIconHandle();

            _nid.hIcon = _hIcon;
            _isCreated = Shell_NotifyIconW(NIM_ADD, ref _nid);
            if (_isCreated)
            {
                _nid.uTimeoutOrVersion = NOTIFYICON_VERSION_4;
                Shell_NotifyIconW(NIM_SETVERSION, ref _nid);
            }
            return IntPtr.Zero;
        }

        if (uMsg == WM_TRAYICON)
        {
            int eventId = (int)(lParam.ToInt64() & 0xFFFF);
            if (eventId == WM_LBUTTONUP || eventId == WM_LBUTTONDBLCLK)
            {
                OnRestoreRequested?.Invoke();
                return IntPtr.Zero;
            }
            if (eventId == WM_RBUTTONUP || eventId == WM_CONTEXTMENU)
            {
                ShowContextMenu();
                return IntPtr.Zero;
            }
        }

        return DefWindowProcW(hWnd, uMsg, wParam, lParam);
    }

    public void ShowContextMenu()
    {
        IntPtr hMenu = CreatePopupMenu();
        if (hMenu == IntPtr.Zero) return;

        IntPtr hBrush = IntPtr.Zero;
        try
        {
            // Фирменный тёмно-фиолетовый фон #141026 (стиль темы MIDNIGHT по стандарту STORM)
            // COLORREF в формате 0x00BBGGRR: Red=0x14, Green=0x10, Blue=0x26 => 0x00261014
            hBrush = CreateSolidBrush(0x00261014);
            if (hBrush != IntPtr.Zero)
            {
                var mi = new MENUINFO
                {
                    cbSize = (uint)Marshal.SizeOf<MENUINFO>(),
                    fMask = MIM_BACKGROUND | MIM_APPLYTOSUBMENUS,
                    hbrBack = hBrush
                };
                SetMenuInfo(hMenu, ref mi);
            }

            // Пункт 1: Развернуть STORM KEYS (жирный системный шрифт Segoe UI 9pt Bold)
            AppendMenuW(hMenu, MF_STRING, (IntPtr)CMD_RESTORE, "Развернуть STORM KEYS");
            SetMenuDefaultItem(hMenu, (uint)CMD_RESTORE, 0);

            // Пункт 2: О программе
            AppendMenuW(hMenu, MF_STRING, (IntPtr)CMD_ABOUT, "О программе");

            // Разделитель
            AppendMenuW(hMenu, MF_SEPARATOR, IntPtr.Zero, string.Empty);

            // Пункт 3: Выход (гарантированное закрытие)
            AppendMenuW(hMenu, MF_STRING, (IntPtr)CMD_EXIT, "Выход");

            GetCursorPos(out POINT pt);

            SetForegroundWindow(_hTrayWnd);

            int cmd = TrackPopupMenuEx(hMenu, TPM_RIGHTBUTTON | TPM_RETURNCMD, pt.X, pt.Y, _hTrayWnd, IntPtr.Zero);
            PostMessageW(_hTrayWnd, 0, IntPtr.Zero, IntPtr.Zero);

            if (cmd == CMD_RESTORE)
            {
                OnRestoreRequested?.Invoke();
            }
            else if (cmd == CMD_ABOUT)
            {
                OnAboutRequested?.Invoke();
            }
            else if (cmd == CMD_EXIT)
            {
                OnExitRequested?.Invoke();
            }
        }
        finally
        {
            if (hBrush != IntPtr.Zero)
            {
                DeleteObject(hBrush);
            }
            DestroyMenu(hMenu);
        }
    }

    public void ShowBalloon(string title, string message)
    {
        if (!_isCreated || _hTrayWnd == IntPtr.Zero) return;

        try
        {
            _nid.uFlags = NIF_INFO;
            _nid.szInfoTitle = title;
            _nid.szInfo = message;
            _nid.dwInfoFlags = NIIF_INFO;
            Shell_NotifyIconW(NIM_MODIFY, ref _nid);
        }
        catch { }
    }

    public void RemoveIcon()
    {
        if (_isCreated)
        {
            Shell_NotifyIconW(NIM_DELETE, ref _nid);
            _isCreated = false;
        }

        if (_hIcon != IntPtr.Zero)
        {
            try { DestroyIcon(_hIcon); } catch { }
            _hIcon = IntPtr.Zero;
        }
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        RemoveIcon();

        if (_hTrayWnd != IntPtr.Zero)
        {
            try
            {
                DestroyWindow(_hTrayWnd);
                _hTrayWnd = IntPtr.Zero;
            }
            catch { }
        }
    }
}
