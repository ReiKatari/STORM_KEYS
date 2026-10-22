using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace StormKeys.Services;

/// <summary>
/// Нативный сервис системного трея со 100% надёжностью, кастомным GDI-меню без системных белых рамок и полей под иконки,
/// динамической синхронизацией цветовой палитры с текущей темой и цветом акцента приложения (по умолчанию MIDNIGHT #141026 + #8B5CF6),
/// поддержкой TaskbarCreated (перезапуск Explorer) и гарантированным закрытием процесса по стандарту экосистемы STORM.
/// </summary>
public sealed class StormTrayService : IDisposable
{
    private const int WM_USER = 0x0400;
    private const int WM_TRAYICON = WM_USER + 2026;
    private const int WM_DESTROY = 0x0002;
    private const int WM_ACTIVATE = 0x0006;
    private const int WM_KILLFOCUS = 0x0008;
    private const int WM_PAINT = 0x000F;
    private const int WM_ERASEBKGND = 0x0014;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_MOUSEMOVE = 0x0200;
    private const int WM_LBUTTONUP = 0x0202;
    private const int WM_LBUTTONDBLCLK = 0x0203;
    private const int WM_RBUTTONUP = 0x0205;
    private const int WM_MOUSELEAVE = 0x02A3;
    private const int WM_CONTEXTMENU = 0x007B;

    private const int WA_INACTIVE = 0;
    private const int VK_RETURN = 0x0D;
    private const int VK_ESCAPE = 0x1B;
    private const int VK_UP = 0x26;
    private const int VK_DOWN = 0x28;

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

    private const uint WS_POPUP = 0x80000000;
    private const uint WS_EX_TOPMOST = 0x00000008;
    private const uint WS_EX_TOOLWINDOW = 0x00000080;

    private const int SW_HIDE = 0;
    private const uint SWP_SHOWWINDOW = 0x0040;
    private static readonly IntPtr HWND_TOPMOST = new(-1);

    private const int SM_CXSMICON = 49;
    private const int SM_CYSMICON = 50;
    private const int SM_CXSCREEN = 0;
    private const int SM_CYSCREEN = 1;

    private const int TRANSPARENT = 1;
    private const uint DT_LEFT = 0x00000000;
    private const uint DT_VCENTER = 0x00000004;
    private const uint DT_SINGLELINE = 0x00000020;
    private const uint DT_NOPREFIX = 0x00000800;

    private const uint TME_LEAVE = 0x00000002;
    private const int IDC_HAND = 32649;
    private const int IDC_ARROW = 32512;

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
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PAINTSTRUCT
    {
        public IntPtr hdc;
        public bool fErase;
        public RECT rcPaint;
        public bool fRestore;
        public bool fIncUpdate;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
        public byte[] rgbReserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TRACKMOUSEEVENT
    {
        public uint cbSize;
        public uint dwFlags;
        public IntPtr hwndTrack;
        public uint dwHoverTime;
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

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr CreateSolidBrush(uint crColor);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern bool DeleteObject(IntPtr hObject);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern int SetBkMode(IntPtr hdc, int iBkMode);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern uint SetTextColor(IntPtr hdc, uint crColor);

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateFontW(
        int cHeight, int cWidth, int cEscapement, int cOrientation, int cWeight,
        uint bItalic, uint bUnderline, uint bStrikeOut, uint iCharSet,
        uint iOutPrecision, uint iClipPrecision, uint iQuality, uint iPitchAndFamily,
        string pszFaceName);

    [DllImport("user32.dll")]
    private static extern IntPtr BeginPaint(IntPtr hWnd, out PAINTSTRUCT lpPaint);

    [DllImport("user32.dll")]
    private static extern bool EndPaint(IntPtr hWnd, ref PAINTSTRUCT lpPaint);

    [DllImport("user32.dll")]
    private static extern int FillRect(IntPtr hDC, ref RECT lprc, IntPtr hbr);

    [DllImport("user32.dll")]
    private static extern int FrameRect(IntPtr hDC, ref RECT lprc, IntPtr hbr);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int DrawTextW(IntPtr hdc, string lpchText, int cchText, ref RECT lprc, uint format);

    [DllImport("user32.dll")]
    private static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern bool InvalidateRect(IntPtr hWnd, IntPtr lpRect, bool bErase);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    private static extern bool TrackMouseEvent(ref TRACKMOUSEEVENT lpEventTrack);

    [DllImport("user32.dll")]
    private static extern IntPtr LoadCursorW(IntPtr hInstance, IntPtr lpCursorName);

    [DllImport("user32.dll")]
    private static extern IntPtr SetCursor(IntPtr hCursor);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForSystem();

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

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
    private readonly WndProcDelegate _menuWndProcDelegate;
    private readonly uint _wmTaskbarCreated;
    private IntPtr _hTrayWnd = IntPtr.Zero;
    private IntPtr _hMenuWnd = IntPtr.Zero;
    private IntPtr _hIcon = IntPtr.Zero;
    private NOTIFYICONDATA _nid;
    private bool _isCreated;
    private bool _isDisposed;

    // Динамическая цветовая палитра меню трея (в формате Win32 COLORREF 0x00BBGGRR)
    // По умолчанию — тема MIDNIGHT: фон #141026, подсветка #2E2652, рамка #8B5CF6, текст #FFFFFF, разделитель #372E63
    private uint _bgColorRef = HexToColorRef("#141026");
    private uint _hoverColorRef = HexToColorRef("#2E2652");
    private uint _borderAccentColorRef = HexToColorRef("#8B5CF6");
    private uint _textPrimaryColorRef = HexToColorRef("#FFFFFF");
    private uint _separatorColorRef = HexToColorRef("#372E63");

    // Локализованные тексты пунктов меню (СТРОГО БЕЗ ИКОНОК И ЭМОДЗИ)
    private string _restoreText = "Развернуть STORM KEYS";
    private string _aboutText = "О программе";
    private string _exitText = "Выход";

    private int _hoverIndex = -1;
    private bool _trackingMouseLeave;

    public Action? OnRestoreRequested { get; set; }
    public Action? OnAboutRequested { get; set; }
    public Action? OnExitRequested { get; set; }

    public StormTrayService(IntPtr ownerHWnd)
    {
        _wndProcDelegate = TrayWindowProc;
        _menuWndProcDelegate = MenuWindowProc;
        _wmTaskbarCreated = RegisterWindowMessageW("TaskbarCreated");

        CreateTrayHelperWindow();
        CreateCustomPopupMenuWindow();
        InitializeTrayIcon();
    }

    /// <summary>
    /// Динамически синхронизирует цвета и локализацию контекстного меню в трее с текущей выбранной темой и акцентом в программе.
    /// </summary>
    public void ApplyTheme(
        string bgHex,
        string hoverHex,
        string borderAccentHex,
        string textPrimaryHex,
        string separatorHex,
        string? restoreText = null,
        string? aboutText = null,
        string? exitText = null)
    {
        _bgColorRef = HexToColorRef(bgHex, 0x00261014);
        _hoverColorRef = HexToColorRef(hoverHex, 0x0052262E);
        _borderAccentColorRef = HexToColorRef(borderAccentHex, 0x00F65C8B);
        _textPrimaryColorRef = HexToColorRef(textPrimaryHex, 0x00FFFFFF);
        _separatorColorRef = HexToColorRef(separatorHex, 0x00632E37);

        if (!string.IsNullOrWhiteSpace(restoreText))
            _restoreText = restoreText;
        if (!string.IsNullOrWhiteSpace(aboutText))
            _aboutText = aboutText;
        if (!string.IsNullOrWhiteSpace(exitText))
            _exitText = exitText;

        if (_hMenuWnd != IntPtr.Zero)
        {
            InvalidateRect(_hMenuWnd, IntPtr.Zero, true);
        }
    }

    public static uint HexToColorRef(string? hex, uint fallback = 0x00261014)
    {
        if (string.IsNullOrWhiteSpace(hex))
            return fallback;

        string clean = hex.Trim().TrimStart('#');
        if (clean.Length == 8)
            clean = clean.Substring(2); // Strip alpha if ARGB

        if (clean.Length != 6)
            return fallback;

        try
        {
            uint r = Convert.ToByte(clean.Substring(0, 2), 16);
            uint g = Convert.ToByte(clean.Substring(2, 2), 16);
            uint b = Convert.ToByte(clean.Substring(4, 2), 16);
            return (b << 16) | (g << 8) | r;
        }
        catch
        {
            return fallback;
        }
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

    private void CreateCustomPopupMenuWindow()
    {
        try
        {
            string menuClassName = $"STORM_KEYS_TRAY_MENU_{Guid.NewGuid():N}";
            var wcx = new WNDCLASSEXW
            {
                cbSize = (uint)Marshal.SizeOf<WNDCLASSEXW>(),
                style = 0x00020000, // CS_DROPSHADOW
                lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_menuWndProcDelegate),
                cbClsExtra = 0,
                cbWndExtra = 0,
                hInstance = Process.GetCurrentProcess().Handle,
                hIcon = IntPtr.Zero,
                hCursor = LoadCursorW(IntPtr.Zero, (IntPtr)IDC_ARROW),
                hbrBackground = IntPtr.Zero,
                lpszMenuName = null,
                lpszClassName = menuClassName,
                hIconSm = IntPtr.Zero
            };

            ushort atom = RegisterClassExW(ref wcx);
            if (atom != 0)
            {
                _hMenuWnd = CreateWindowExW(
                    WS_EX_TOPMOST | WS_EX_TOOLWINDOW,
                    menuClassName,
                    "STORM_KEYS_TRAY_MENU",
                    WS_POPUP,
                    -32000, -32000, 232, 112,
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
                if (_hMenuWnd != IntPtr.Zero)
                    ShowWindow(_hMenuWnd, SW_HIDE);
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

    private double GetDpiScale()
    {
        try
        {
            uint dpi = GetDpiForSystem();
            if (dpi >= 96) return dpi / 96.0;
        }
        catch { }
        return 1.0;
    }

    private void GetMenuLayout(out int width, out int height, out int rowHeight, out int sepHeight, out int padY, out int padX)
    {
        double scale = GetDpiScale();
        width = (int)Math.Round(236 * scale);
        rowHeight = (int)Math.Round(32 * scale);
        sepHeight = (int)Math.Round(9 * scale);
        padY = (int)Math.Round(5 * scale);
        padX = (int)Math.Round(14 * scale);
        height = padY * 2 + rowHeight * 3 + sepHeight;
    }

    private int HitTestItem(int x, int y)
    {
        GetMenuLayout(out int w, out int h, out int rowH, out int sepH, out int padY, out _);
        if (x < 1 || x >= w - 1 || y < padY || y >= h - padY)
            return -1;

        if (y >= padY && y < padY + rowH)
            return 0; // Развернуть STORM KEYS
        if (y >= padY + rowH && y < padY + rowH * 2)
            return 1; // О программе
        int item2Top = padY + rowH * 2 + sepH;
        if (y >= item2Top && y < item2Top + rowH)
            return 2; // Выход

        return -1;
    }

    private IntPtr MenuWindowProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam)
    {
        switch (uMsg)
        {
            case WM_ERASEBKGND:
                return (IntPtr)1;

            case WM_ACTIVATE:
            {
                int state = (int)(wParam.ToInt64() & 0xFFFF);
                if (state == WA_INACTIVE)
                {
                    _hoverIndex = -1;
                    ShowWindow(hWnd, SW_HIDE);
                }
                return IntPtr.Zero;
            }

            case WM_KILLFOCUS:
            {
                _hoverIndex = -1;
                ShowWindow(hWnd, SW_HIDE);
                return IntPtr.Zero;
            }

            case WM_KEYDOWN:
            {
                int vk = (int)wParam.ToInt64();
                if (vk == VK_ESCAPE)
                {
                    _hoverIndex = -1;
                    ShowWindow(hWnd, SW_HIDE);
                    return IntPtr.Zero;
                }
                if (vk == VK_DOWN)
                {
                    _hoverIndex = (_hoverIndex + 1) % 3;
                    InvalidateRect(hWnd, IntPtr.Zero, false);
                    return IntPtr.Zero;
                }
                if (vk == VK_UP)
                {
                    _hoverIndex = _hoverIndex <= 0 ? 2 : _hoverIndex - 1;
                    InvalidateRect(hWnd, IntPtr.Zero, false);
                    return IntPtr.Zero;
                }
                if (vk == VK_RETURN && _hoverIndex >= 0 && _hoverIndex <= 2)
                {
                    int target = _hoverIndex;
                    _hoverIndex = -1;
                    ShowWindow(hWnd, SW_HIDE);
                    ExecuteMenuAction(target);
                    return IntPtr.Zero;
                }
                break;
            }

            case WM_MOUSEMOVE:
            {
                int x = (short)(lParam.ToInt64() & 0xFFFF);
                int y = (short)((lParam.ToInt64() >> 16) & 0xFFFF);

                if (!_trackingMouseLeave)
                {
                    var tme = new TRACKMOUSEEVENT
                    {
                        cbSize = (uint)Marshal.SizeOf<TRACKMOUSEEVENT>(),
                        dwFlags = TME_LEAVE,
                        hwndTrack = hWnd,
                        dwHoverTime = 0
                    };
                    TrackMouseEvent(ref tme);
                    _trackingMouseLeave = true;
                }

                int hit = HitTestItem(x, y);
                if (hit >= 0)
                {
                    SetCursor(LoadCursorW(IntPtr.Zero, (IntPtr)IDC_HAND));
                }

                if (hit != _hoverIndex)
                {
                    _hoverIndex = hit;
                    InvalidateRect(hWnd, IntPtr.Zero, false);
                }
                return IntPtr.Zero;
            }

            case WM_MOUSELEAVE:
            {
                _trackingMouseLeave = false;
                if (_hoverIndex != -1)
                {
                    _hoverIndex = -1;
                    InvalidateRect(hWnd, IntPtr.Zero, false);
                }
                return IntPtr.Zero;
            }

            case WM_LBUTTONUP:
            {
                int x = (short)(lParam.ToInt64() & 0xFFFF);
                int y = (short)((lParam.ToInt64() >> 16) & 0xFFFF);
                int hit = HitTestItem(x, y);
                _hoverIndex = -1;
                ShowWindow(hWnd, SW_HIDE);
                if (hit >= 0)
                {
                    ExecuteMenuAction(hit);
                }
                return IntPtr.Zero;
            }

            case WM_PAINT:
            {
                IntPtr hdc = BeginPaint(hWnd, out PAINTSTRUCT ps);
                if (hdc != IntPtr.Zero)
                {
                    DrawCustomContextMenu(hWnd, hdc);
                }
                EndPaint(hWnd, ref ps);
                return IntPtr.Zero;
            }
        }

        return DefWindowProcW(hWnd, uMsg, wParam, lParam);
    }

    private void ExecuteMenuAction(int index)
    {
        if (index == 0)
            OnRestoreRequested?.Invoke();
        else if (index == 1)
            OnAboutRequested?.Invoke();
        else if (index == 2)
            OnExitRequested?.Invoke();
    }

    private void DrawCustomContextMenu(IntPtr hWnd, IntPtr hdc)
    {
        GetClientRect(hWnd, out RECT rcClient);
        GetMenuLayout(out int w, out int h, out int rowH, out int sepH, out int padY, out int padX);
        double scale = GetDpiScale();

        IntPtr hBgBrush = CreateSolidBrush(_bgColorRef);
        IntPtr hBorderBrush = CreateSolidBrush(_borderAccentColorRef);
        IntPtr hHoverBrush = CreateSolidBrush(_hoverColorRef);
        IntPtr hSepBrush = CreateSolidBrush(_separatorColorRef);

        int fontHeight = -(int)Math.Round(12.0 * scale); // Segoe UI 9pt
        IntPtr hFontBold = CreateFontW(fontHeight, 0, 0, 0, 700 /*FW_BOLD*/, 0, 0, 0, 1, 0, 0, 5 /*CLEARTYPE_QUALITY*/, 0, "Segoe UI");
        IntPtr hFontReg = CreateFontW(fontHeight, 0, 0, 0, 400 /*FW_NORMAL*/, 0, 0, 0, 1, 0, 0, 5 /*CLEARTYPE_QUALITY*/, 0, "Segoe UI");

        try
        {
            // 1. Фон в цвет текущей темы (MIDNIGHT #141026 по умолчанию)
            FillRect(hdc, ref rcClient, hBgBrush);

            // 2. Тонкая 1px неоновая рамка в цвет текущего акцента (#8B5CF6 по умолчанию)
            FrameRect(hdc, ref rcClient, hBorderBrush);

            SetBkMode(hdc, TRANSPARENT);
            SetTextColor(hdc, _textPrimaryColorRef);

            // Массив пунктов меню (СТРОГО БЕЗ ИКОНОК И ЭМОДЗИ, ShowImageMargin = false)
            string[] labels = { _restoreText, _aboutText, _exitText };
            int[] itemTops = {
                padY,
                padY + rowH,
                padY + rowH * 2 + sepH
            };

            for (int i = 0; i < 3; i++)
            {
                int top = itemTops[i];
                int bottom = top + rowH;

                if (_hoverIndex == i)
                {
                    var rcHover = new RECT
                    {
                        Left = 2,
                        Top = top,
                        Right = rcClient.Right - 2,
                        Bottom = bottom
                    };
                    FillRect(hdc, ref rcHover, hHoverBrush);

                    // Тонкая акцентная полоска слева при наведении
                    var rcIndicator = new RECT
                    {
                        Left = 2,
                        Top = top + (int)Math.Round(5 * scale),
                        Right = 2 + (int)Math.Round(3 * scale),
                        Bottom = bottom - (int)Math.Round(5 * scale)
                    };
                    FillRect(hdc, ref rcIndicator, hBorderBrush);
                }

                IntPtr oldFont = SelectObject(hdc, i == 0 ? hFontBold : hFontReg);
                var rcText = new RECT
                {
                    Left = padX,
                    Top = top,
                    Right = rcClient.Right - padX,
                    Bottom = bottom
                };
                DrawTextW(hdc, labels[i], -1, ref rcText, DT_LEFT | DT_VCENTER | DT_SINGLELINE | DT_NOPREFIX);
                SelectObject(hdc, oldFont);
            }

            // 3. Горизонтальный разделитель перед пунктом «Выход»
            int sepY = padY + rowH * 2 + sepH / 2;
            var rcSep = new RECT
            {
                Left = padX - 2,
                Top = sepY,
                Right = rcClient.Right - (padX - 2),
                Bottom = sepY + 1
            };
            FillRect(hdc, ref rcSep, hSepBrush);
        }
        finally
        {
            if (hFontBold != IntPtr.Zero) DeleteObject(hFontBold);
            if (hFontReg != IntPtr.Zero) DeleteObject(hFontReg);
            if (hBgBrush != IntPtr.Zero) DeleteObject(hBgBrush);
            if (hBorderBrush != IntPtr.Zero) DeleteObject(hBorderBrush);
            if (hHoverBrush != IntPtr.Zero) DeleteObject(hHoverBrush);
            if (hSepBrush != IntPtr.Zero) DeleteObject(hSepBrush);
        }
    }

    public void ShowContextMenu()
    {
        if (_hMenuWnd == IntPtr.Zero) return;

        GetCursorPos(out POINT pt);
        GetMenuLayout(out int w, out int h, out _, out _, out _, out _);

        int screenW = GetSystemMetrics(SM_CXSCREEN);
        int screenH = GetSystemMetrics(SM_CYSCREEN);

        int x = pt.X;
        int y = pt.Y - h;

        if (x + w > screenW - 8)
            x = screenW - w - 8;
        if (x < 8)
            x = 8;

        if (y < 8)
            y = pt.Y + 4;
        if (y + h > screenH - 40)
            y = screenH - h - 48;

        _hoverIndex = -1;
        SetWindowPos(_hMenuWnd, HWND_TOPMOST, x, y, w, h, SWP_SHOWWINDOW);
        SetForegroundWindow(_hMenuWnd);
        InvalidateRect(_hMenuWnd, IntPtr.Zero, true);
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

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        if (_isCreated)
        {
            try
            {
                Shell_NotifyIconW(NIM_DELETE, ref _nid);
            }
            catch { }
            _isCreated = false;
        }

        if (_hIcon != IntPtr.Zero)
        {
            try { DestroyIcon(_hIcon); } catch { }
            _hIcon = IntPtr.Zero;
        }

        if (_hMenuWnd != IntPtr.Zero)
        {
            try { DestroyWindow(_hMenuWnd); } catch { }
            _hMenuWnd = IntPtr.Zero;
        }

        if (_hTrayWnd != IntPtr.Zero)
        {
            try { DestroyWindow(_hTrayWnd); } catch { }
            _hTrayWnd = IntPtr.Zero;
        }
    }
}
