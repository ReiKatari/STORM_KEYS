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
    public static MainViewModel ViewModel { get; private set; } = null!;

    public App()
    {
        InitializeComponent();

        // Инициализация сервисов экосистемы STORM KEYS
        CryptoService = new CryptoService();
        VaultService = new VaultService(CryptoService);
        ImportExportService = new ImportExportService();
        NotificationService = new NotificationService();
        CertificateService = new StormCertificateService();

        ViewModel = new MainViewModel(
            VaultService,
            CryptoService,
            ImportExportService,
            NotificationService,
            CertificateService);
    }

    protected override async void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        await VaultService.InitializeAsync();

        _window = new MainWindow();
        _window.Activate();
    }
}
