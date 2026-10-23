using System;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using StormKeys.Helpers;
using StormKeys.Services;
using StormKeys.ViewModels;

namespace StormKeys;

public sealed partial class MainWindow : Window
{
    private bool _isQuittingFromTray;
    private StormTrayService? _trayService;
    private IntPtr _hWnd;
    private WndProcDelegate? _wndProcDelegate;
    private IntPtr _origWndProc;

    public StormTrayService? TrayService => _trayService;
    public static MainWindow? Instance { get; private set; }
    public ActiveWindowInfo LastExternalWindow { get; private set; } = new(IntPtr.Zero, string.Empty, string.Empty);

    private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("shell32.dll", SetLastError = true)]
    private static extern int SetCurrentProcessExplicitAppUserModelID([MarshalAs(UnmanagedType.LPWStr)] string AppID);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern IntPtr LoadImage(IntPtr hinst, string lpszName, uint uType, int cxDesired, int cyDesired, uint fuLoad);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint dwAffinity);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", EntryPoint = "CallWindowProcW")]
    private static extern IntPtr CallWindowProc(IntPtr lpPrevWndFunc, IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    private const uint WM_SETICON = 0x0080;
    private const uint WM_HOTKEY = 0x0312;
    private const uint WM_ACTIVATE = 0x0006;
    private const int GWLP_WNDPROC = -4;

    private const uint MOD_CONTROL = 0x0002;
    private const uint MOD_SHIFT = 0x0004;
    private const uint MOD_NOREPEAT = 0x4000;
    private const uint VK_SPACE = 0x20;
    private const uint VK_L = 0x4C;

    private const int HOTKEY_ID_SPOTLIGHT = 9001; // Ctrl + Shift + Space
    private const int HOTKEY_ID_PANIC = 9002;     // Ctrl + Shift + L

    private const uint WDA_NONE = 0x00000000;
    private const uint WDA_EXCLUDEFROMCAPTURE = 0x00000011;

    private const IntPtr ICON_SMALL = 0;
    private const IntPtr ICON_BIG = (IntPtr)1;
    private const uint IMAGE_ICON = 1;
    private const uint LR_LOADFROMFILE = 0x00000010;

    public MainWindow()
    {
        Instance = this;

        try
        {
            SetCurrentProcessExplicitAppUserModelID("STORM.TEAM.STORM.KEYS");
        }
        catch { }

        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        Title = "STORM KEYS 1.0.5";
        AppWindow.Title = "STORM KEYS 1.0.5";

        // Получаем дескриптор Win32 окна
        _hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);

        // Явная привязка иконки в шапке окна для Unpackaged WinUI 3
        try
        {
            var logoPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Square44x44Logo.scale-200.png");
            if (File.Exists(logoPath))
            {
                AppTitleBar.IconSource = new ImageIconSource
                {
                    ImageSource = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(logoPath))
                };
            }
        }
        catch { }

        // Загрузка и установка иконки окна и панели задач
        try
        {
            var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
            if (File.Exists(iconPath))
            {
                AppWindow.SetIcon(iconPath);

                if (_hWnd != IntPtr.Zero)
                {
                    var hSmall = LoadImage(IntPtr.Zero, iconPath, IMAGE_ICON, 16, 16, LR_LOADFROMFILE);
                    var hBig = LoadImage(IntPtr.Zero, iconPath, IMAGE_ICON, 32, 32, LR_LOADFROMFILE);
                    if (hSmall != IntPtr.Zero)
                        SendMessage(_hWnd, WM_SETICON, ICON_SMALL, hSmall);
                    if (hBig != IntPtr.Zero)
                        SendMessage(_hWnd, WM_SETICON, ICON_BIG, hBig);
                }
            }
        }
        catch { }

        // DPI-зависимый размер окна по умолчанию (1360x840 DIPs с учётом масштаба экрана и центрированием)
        ApplyDefaultWindowSize();

        // Регистрация глобальных горячих клавиш: Ctrl+Shift+Space (Spotlight) и Ctrl+Shift+L (Экстренная блокировка)
        if (_hWnd != IntPtr.Zero)
        {
            try
            {
                _wndProcDelegate = CustomWndProc;
                _origWndProc = SetWindowLongPtr(_hWnd, GWLP_WNDPROC, Marshal.GetFunctionPointerForDelegate(_wndProcDelegate));
                RegisterHotKey(_hWnd, HOTKEY_ID_SPOTLIGHT, MOD_CONTROL | MOD_SHIFT | MOD_NOREPEAT, VK_SPACE);
                RegisterHotKey(_hWnd, HOTKEY_ID_PANIC, MOD_CONTROL | MOD_SHIFT | MOD_NOREPEAT, VK_L);
            }
            catch { }
        }

        // Стандарт STORM: Закрытие в системный трей (Close-to-Tray)
        AppWindow.Closing += (sender, args) =>
        {
            if (!_isQuittingFromTray)
            {
                args.Cancel = true; // Отменяем закрытие процесса
                AppWindow.Hide();   // Сворачиваем в системный трей
            }
        };

        // Инициализация нативного сервиса системного трея со 100% надёжностью
        if (_hWnd != IntPtr.Zero)
        {
            _trayService = new StormTrayService(_hWnd)
            {
                OnRestoreRequested = RestoreWindow,
                OnAboutRequested = ShowAboutDialog,
                OnExitRequested = ExitApplication
            };
        }

        // Навигация на главную страницу
        RootFrame.Navigate(typeof(MainPage));
    }

    private IntPtr CustomWndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WM_HOTKEY)
        {
            int id = wParam.ToInt32();
            if (id == HOTKEY_ID_SPOTLIGHT)
            {
                var prev = AutoTypeHelper.GetPreviousWindowInfo(_hWnd);
                if (!string.IsNullOrWhiteSpace(prev.Title))
                    LastExternalWindow = prev;

                DispatcherQueue.TryEnqueue(() =>
                {
                    RestoreWindow();
                    if (RootFrame.Content is MainPage page)
                    {
                        page.ViewModel?.UpdateActiveWindowMatch(LastExternalWindow);
                        page.FocusSearchBoxForSpotlight();
                    }
                });
                return IntPtr.Zero;
            }
            else if (id == HOTKEY_ID_PANIC)
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    if (RootFrame.Content is MainPage page && page.ViewModel != null)
                    {
                        _ = page.ViewModel.ExecutePanicLockAsync();
                    }
                });
                return IntPtr.Zero;
            }
        }
        else if (msg == WM_ACTIVATE && (wParam.ToInt64() & 0xFFFF) != 0)
        {
            var prev = AutoTypeHelper.GetPreviousWindowInfo(_hWnd);
            if (!string.IsNullOrWhiteSpace(prev.Title))
            {
                LastExternalWindow = prev;
                DispatcherQueue.TryEnqueue(() =>
                {
                    if (RootFrame.Content is MainPage page)
                    {
                        page.ViewModel?.UpdateActiveWindowMatch(LastExternalWindow);
                    }
                });
            }
        }

        return CallWindowProc(_origWndProc, hWnd, msg, wParam, lParam);
    }

    public void ApplyDefaultWindowSize()
    {
        try
        {
            double scale = _hWnd != IntPtr.Zero ? Math.Max(1.0, GetDpiForWindow(_hWnd) / 96.0) : 1.0;
            int screenW = Math.Max(1280, GetSystemMetrics(0));
            int screenH = Math.Max(800, GetSystemMetrics(1));
            int targetW = Math.Min((int)Math.Round(1360 * scale), screenW - 48);
            int targetH = Math.Min((int)Math.Round(840 * scale), screenH - 64);
            AppWindow.Resize(new Windows.Graphics.SizeInt32(targetW, targetH));
            AppWindow.Move(new Windows.Graphics.PointInt32(Math.Max(0, (screenW - targetW) / 2), Math.Max(0, (screenH - targetH) / 2)));
        }
        catch
        {
            AppWindow.Resize(new Windows.Graphics.SizeInt32(1360, 840));
        }
    }

    public void SetMiniMode(bool miniMode)
    {
        try
        {
            if (AppWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.IsAlwaysOnTop = miniMode;
            }

            if (miniMode)
            {
                double scale = _hWnd != IntPtr.Zero ? Math.Max(1.0, GetDpiForWindow(_hWnd) / 96.0) : 1.0;
                int screenW = Math.Max(1280, GetSystemMetrics(0));
                int w = (int)Math.Round(560 * scale);
                int h = (int)Math.Round(760 * scale);
                AppWindow.Resize(new Windows.Graphics.SizeInt32(w, h));
                AppWindow.Move(new Windows.Graphics.PointInt32(Math.Max(20, screenW - w - 32), 48));
            }
            else
            {
                ApplyDefaultWindowSize();
            }
        }
        catch { }
    }

    public void SetScreenCaptureProtection(bool enabled)
    {
        if (_hWnd == IntPtr.Zero) return;
        try
        {
            SetWindowDisplayAffinity(_hWnd, enabled ? WDA_EXCLUDEFROMCAPTURE : WDA_NONE);
        }
        catch { }
    }

    public void RestoreWindow()
    {
        AppWindow.Show();
        AppWindow.MoveInZOrderAtTop();
        Activate();
    }

    public void ShowAboutDialog()
    {
        RestoreWindow();
        if (RootFrame.Content is MainPage page && page.ViewModel != null)
        {
            page.ViewModel.IsAboutDialogVisible = true;
        }
    }

    public void ExitApplication()
    {
        _isQuittingFromTray = true;

        if (_hWnd != IntPtr.Zero)
        {
            try
            {
                UnregisterHotKey(_hWnd, HOTKEY_ID_SPOTLIGHT);
                UnregisterHotKey(_hWnd, HOTKEY_ID_PANIC);
            }
            catch { }
        }

        try
        {
            _trayService?.Dispose();
        }
        catch { }

        try
        {
            this.Close();
        }
        catch { }

        try
        {
            Application.Current.Exit();
        }
        catch { }

        // Гарантированное завершение процесса приложения
        Environment.Exit(0);
    }
}
