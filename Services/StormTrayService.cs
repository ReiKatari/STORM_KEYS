using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace StormKeys.Services;

/// <summary>
/// Нативный сервис системного трея со 100% надежностью, тёмным оформлением MIDNIGHT (#141026)
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

    private const int NIM_ADD = 0x00000000;
    private const int NIM_MODIFY = 0x00000001;
    private const int NIM_DELETE = 0x00000002;

    private const int NIF_MESSAGE = 0x00000001;
    private const int NIF_ICON = 0x00000002;
    private const int NIF_TIP = 0x00000004;

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
    private const uint WS_VISIBLE = 0x10000000;
    private const uint WS_EX_TOOLWINDOW = 0x00000080;

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

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIconW(int dwMessage, ref NOTIFYICONDATA lpdata);

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
    private static extern IntPtr CreateWindowExW(
        uint dwExStyle, string lpClassName, string lpWindowName, uint dwStyle,
        int x, int y, int nWidth, int nHeight, IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("comctl32.dll", SetLastError = true)]
    private static extern bool SetWindowSubclass(IntPtr hWnd, SubclassProc pfnSubclass, UIntPtr uIdSubclass, IntPtr dwRefData);

    [DllImport("comctl32.dll", SetLastError = true)]
    private static extern bool RemoveWindowSubclass(IntPtr hWnd, SubclassProc pfnSubclass, UIntPtr uIdSubclass);

    [DllImport("comctl32.dll")]
    private static extern IntPtr DefSubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);

    [DllImport("uxtheme.dll", EntryPoint = "#135", SetLastError = true)]
    private static extern int SetPreferredAppMode(int appMode);

    [DllImport("uxtheme.dll", EntryPoint = "#133", SetLastError = true)]
    private static extern bool AllowDarkModeForWindow(IntPtr hWnd, bool allow);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern IntPtr LoadImage(IntPtr hinst, string lpszName, uint uType, int cxDesired, int cyDesired, uint fuLoad);

    private delegate IntPtr SubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, UIntPtr uIdSubclass, IntPtr dwRefData);

    private readonly IntPtr _hWnd;
    private readonly SubclassProc _subclassDelegate;
    private NOTIFYICONDATA _nid;
    private IntPtr _hHelperWnd = IntPtr.Zero;
    private bool _isCreated;
    private bool _isDisposed;

    public Action? OnRestoreRequested { get; set; }
    public Action? OnAboutRequested { get; set; }
    public Action? OnExitRequested { get; set; }

    public StormTrayService(IntPtr hWnd)
    {
        _hWnd = hWnd;
        _subclassDelegate = WindowSubclassProc;

        try
        {
            // Системный тёмный режим для Win32 меню (AllowDark = 1, ForceDark = 2)
            SetPreferredAppMode(2);
            AllowDarkModeForWindow(_hWnd, true);
        }
        catch { }

        // Создаем вспомогательное невидимое окно для гарантированной активации меню, когда главное окно скрыто (AppWindow.Hide())
        try
        {
            _hHelperWnd = CreateWindowExW(
                WS_EX_TOOLWINDOW,
                "STATIC",
                "StormTrayHelper",
                WS_POPUP | WS_VISIBLE,
                -32000, -32000, 1, 1,
                IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);

            if (_hHelperWnd != IntPtr.Zero)
            {
                AllowDarkModeForWindow(_hHelperWnd, true);
            }
        }
        catch { }

        SetWindowSubclass(_hWnd, _subclassDelegate, (UIntPtr)2026, IntPtr.Zero);
        InitializeTrayIcon();
    }

    private void InitializeTrayIcon()
    {
        IntPtr hIcon = IntPtr.Zero;
        try
        {
            string iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
            if (File.Exists(iconPath))
            {
                hIcon = LoadImage(IntPtr.Zero, iconPath, 1 /*IMAGE_ICON*/, 16, 16, 0x00000010 /*LR_LOADFROMFILE*/);
            }
        }
        catch { }

        _nid = new NOTIFYICONDATA
        {
            cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd = _hWnd,
            uID = 1,
            uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP,
            uCallbackMessage = WM_TRAYICON,
            hIcon = hIcon,
            szTip = "STORM KEYS"
        };

        _isCreated = Shell_NotifyIconW(NIM_ADD, ref _nid);
    }

    private IntPtr WindowSubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, UIntPtr uIdSubclass, IntPtr dwRefData)
    {
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

        return DefSubclassProc(hWnd, uMsg, wParam, lParam);
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

            // Используем helper window для гарантированного SetForegroundWindow, даже если главное окно скрыто
            IntPtr trackingWnd = _hHelperWnd != IntPtr.Zero ? _hHelperWnd : _hWnd;
            SetForegroundWindow(trackingWnd);

            int cmd = TrackPopupMenuEx(hMenu, TPM_RIGHTBUTTON | TPM_RETURNCMD, pt.X, pt.Y, trackingWnd, IntPtr.Zero);
            PostMessageW(trackingWnd, 0, IntPtr.Zero, IntPtr.Zero);

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

    public void RemoveIcon()
    {
        if (_isCreated)
        {
            Shell_NotifyIconW(NIM_DELETE, ref _nid);
            _isCreated = false;
        }
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        RemoveIcon();
        RemoveWindowSubclass(_hWnd, _subclassDelegate, (UIntPtr)2026);

        if (_hHelperWnd != IntPtr.Zero)
        {
            try
            {
                DestroyWindow(_hHelperWnd);
                _hHelperWnd = IntPtr.Zero;
            }
            catch { }
        }
    }
}
