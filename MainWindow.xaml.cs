using System;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using StormKeys.Services;
using StormKeys.ViewModels;

namespace StormKeys;

public sealed partial class MainWindow : Window
{
    private bool _isQuittingFromTray;
    private StormTrayService? _trayService;

    public StormTrayService? TrayService => _trayService;
    public static MainWindow? Instance { get; private set; }

    [DllImport("shell32.dll", SetLastError = true)]
    private static extern int SetCurrentProcessExplicitAppUserModelID([MarshalAs(UnmanagedType.LPWStr)] string AppID);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern IntPtr LoadImage(IntPtr hinst, string lpszName, uint uType, int cxDesired, int cyDesired, uint fuLoad);

    private const uint WM_SETICON = 0x0080;
    private const IntPtr ICON_SMALL = 0;
    private const IntPtr ICON_BIG = (IntPtr)1;
    private const uint IMAGE_ICON = 1;
    private const uint LR_LOADFROMFILE = 0x00000010;
    private const uint LR_DEFAULTSIZE = 0x00000040;

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

        Title = "STORM KEYS 1.0.4";
        AppWindow.Title = "STORM KEYS 1.0.4";

        // Получаем дескриптор Win32 окна
        var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);

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

                if (hWnd != IntPtr.Zero)
                {
                    var hSmall = LoadImage(IntPtr.Zero, iconPath, IMAGE_ICON, 16, 16, LR_LOADFROMFILE);
                    var hBig = LoadImage(IntPtr.Zero, iconPath, IMAGE_ICON, 32, 32, LR_LOADFROMFILE);
                    if (hSmall != IntPtr.Zero)
                        SendMessage(hWnd, WM_SETICON, ICON_SMALL, hSmall);
                    if (hBig != IntPtr.Zero)
                        SendMessage(hWnd, WM_SETICON, ICON_BIG, hBig);
                }
            }
        }
        catch { }

        // Размер окна по умолчанию согласно стандарту STORM (Section 8: Desktop Default Geometry >= 1280x800)
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1280, 800));

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
        if (hWnd != IntPtr.Zero)
        {
            _trayService = new StormTrayService(hWnd)
            {
                OnRestoreRequested = RestoreWindow,
                OnAboutRequested = ShowAboutDialog,
                OnExitRequested = ExitApplication
            };
        }

        // Навигация на главную страницу
        RootFrame.Navigate(typeof(MainPage));
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
