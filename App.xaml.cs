using System;
using Microsoft.UI.Xaml;
using StormKeys.Services;
using StormKeys.ViewModels;

namespace StormKeys;

public partial class App : Application
{
    private Window? _window;

    public static ICryptoService CryptoService { get; private set; } = null!;
    public static IVaultService VaultService { get; private set; } = null!;
    public static IImportExportService ImportExportService { get; private set; } = null!;
    public static INotificationService NotificationService { get; private set; } = null!;
    public static IStormCertificateService CertificateService { get; private set; } = null!;
    public static IUpdateService UpdateService { get; private set; } = null!;
    public static MainViewModel ViewModel { get; private set; } = null!;

    [System.Runtime.InteropServices.DllImport("shell32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
    private static extern int SetCurrentProcessExplicitAppUserModelID(string appID);

    public App()
    {
        try
        {
            SetCurrentProcessExplicitAppUserModelID("STORM.KEYS");
        }
        catch { }

        InitializeComponent();

        // Инициализация сервисов экосистемы STORM KEYS
        CryptoService = new CryptoService();
        VaultService = new VaultService(CryptoService);
        ImportExportService = new ImportExportService();
        NotificationService = new NotificationService();
        CertificateService = new StormCertificateService();
        UpdateService = new UpdateService();

        ViewModel = new MainViewModel(
            VaultService,
            CryptoService,
            ImportExportService,
            NotificationService,
            CertificateService,
            UpdateService);
    }

    protected override async void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        await VaultService.InitializeAsync();

        _window = new MainWindow();
        _window.Activate();
    }
}
