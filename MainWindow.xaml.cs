using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using StormKeys.ViewModels;

namespace StormKeys;

public sealed partial class MainWindow : Window
{
    private bool _isQuittingFromTray;

    public static MainWindow? Instance { get; private set; }

    public MainWindow()
    {
        Instance = this;
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        AppWindow.SetIcon("Assets/AppIcon.ico");
        AppWindow.Title = "STORM KEYS";

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

        // Клик левой кнопкой мыши по иконке трея разворачивает окно
        StormTrayIcon.LeftClickCommand = new CommunityToolkit.Mvvm.Input.RelayCommand(RestoreWindow);

        // Навигация на главную страницу
        RootFrame.Navigate(typeof(MainPage));
    }

    public void RestoreWindow()
    {
        AppWindow.Show();
        AppWindow.MoveInZOrderAtTop();
        Activate();
    }

    private void MenuOpenApp_Click(object sender, RoutedEventArgs e)
    {
        RestoreWindow();
    }

    private void MenuLockApp_Click(object sender, RoutedEventArgs e)
    {
        if (RootFrame.Content is MainPage page && page.ViewModel != null)
        {
            _ = page.ViewModel.LockVaultCommand.ExecuteAsync(null);
        }
        RestoreWindow();
    }

    private void MenuExitApp_Click(object sender, RoutedEventArgs e)
    {
        _isQuittingFromTray = true;
        try
        {
            StormTrayIcon.Dispose();
        }
        catch { }

        this.Close();
        Application.Current.Exit();
    }
}
