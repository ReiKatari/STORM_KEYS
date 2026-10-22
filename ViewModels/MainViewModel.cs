using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using StormKeys.Helpers;
using StormKeys.Models;
using StormKeys.Services;

namespace StormKeys.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly IVaultService _vaultService;
    private readonly ICryptoService _cryptoService;
    private readonly IImportExportService _importExportService;
    private readonly INotificationService _notificationService;
    private readonly IStormCertificateService _certService;
    private readonly IUpdateService _updateService;
    private readonly ITotpService _totpService;
    private readonly IWindowsHelloService _windowsHelloService;
    private readonly IPwnedPasswordsService _pwnedPasswordsService;
    private DispatcherQueue? _dispatcherQueue;
    private DispatcherTimer? _totpTimer;

    // Обновления (Auto-Update System)
    [ObservableProperty]
    private bool _isUpdateAvailable;

    [ObservableProperty]
    private string _updateVersion = string.Empty;

    [ObservableProperty]
    private string _updateChangelog = string.Empty;

    [ObservableProperty]
    private string _updateDownloadUrl = string.Empty;

    [ObservableProperty]
    private string _updateFileName = string.Empty;

    [ObservableProperty]
    private bool _isCheckingUpdate;

    [ObservableProperty]
    private double _updateProgress;

    [ObservableProperty]
    private string _updateStatusText = $"Установлена актуальная версия ({UpdateService.CurrentVersionString})";

    [ObservableProperty]
    private bool _isAboutDialogVisible;

    // Компактный / комфортный вид и фильтрация
    [ObservableProperty]
    private bool _isCompactView;

    [ObservableProperty]
    private string _activeFilterChip = "All";

    [ObservableProperty]
    private int _favoritesCount;

    [ObservableProperty]
    private int _breachedCount;

    [ObservableProperty]
    private int _weakPasswordsCount;

    [ObservableProperty]
    private int _duplicatePasswordsCount;

    [ObservableProperty]
    private bool _isHealthDashboardVisible;

    [ObservableProperty]
    private string _selectedSortOrder = "По приоритету срока";

    [ObservableProperty]
    private string _secretEntropyText = "0 бит";

    [ObservableProperty]
    private string _secretCrackTimeText = "—";

    [ObservableProperty]
    private string _editTagsText = string.Empty;

    [ObservableProperty]
    private string _editCustomFieldsText = string.Empty;

    [ObservableProperty]
    private bool _hasPasswordHistory;

    [ObservableProperty]
    private string _editPasswordHistoryText = string.Empty;

    [ObservableProperty]
    private bool _canUndoDelete;

    private KeyEntry? _lastDeletedEntry;

    [ObservableProperty]
    private string _oldMasterPasswordInput = string.Empty;

    [ObservableProperty]
    private string _newMasterPasswordInput = string.Empty;

    [ObservableProperty]
    private Thickness _cardPadding = new(16, 12, 16, 12);

    private DateTimeOffset _lastActivityTime = DateTimeOffset.Now;

    public void RecordUserActivity()
    {
        _lastActivityTime = DateTimeOffset.Now;
    }

    // Состояния хранилища
    [ObservableProperty]
    private bool _isVaultUninitialized;

    [ObservableProperty]
    private bool _isVaultLocked = true;

    [ObservableProperty]
    private bool _isVaultEmpty = true;

    [ObservableProperty]
    private string _masterPasswordInput = string.Empty;

    [ObservableProperty]
    private string _confirmPasswordInput = string.Empty;

    [ObservableProperty]
    private string _masterPasswordErrorMessage = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    // Поиск и фильтры
    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private string _selectedCategoryFilter = "Все категории";

    [ObservableProperty]
    private string _selectedExpirationFilter = "Все сроки";

    // Списки записей
    public ObservableCollection<KeyEntry> FilteredEntries { get; } = new();

    [ObservableProperty]
    private KeyEntry? _selectedEntry;

    // Счетчики метрик
    [ObservableProperty]
    private int _totalEntriesCount;

    [ObservableProperty]
    private int _expiringSoonCount;

    [ObservableProperty]
    private int _expiredCount;

    [ObservableProperty]
    private int _activeCount;

    [ObservableProperty]
    private double _activeRatio = 1.0;

    [ObservableProperty]
    private double _expiringRatio = 0.0;

    [ObservableProperty]
    private double _expiredRatio = 0.0;

    [ObservableProperty]
    private string _healthScoreText = "100%";

    [ObservableProperty]
    private string _cloudSyncStatusText = "Локальное хранилище";

    [ObservableProperty]
    private bool _isCloudSyncConfigured;

    [ObservableProperty]
    private string _certificateStatus = "STORM CERTIFIED";

    // Редактирование / Создание записи (Максимально прокачанный диалог 1.0.4)
    [ObservableProperty]
    private bool _isEditDialogVisible;

    [ObservableProperty]
    private bool _isNewEntry;

    [ObservableProperty]
    private string _editDialogHeader = "Новый ключ / пароль";

    [ObservableProperty]
    private string _editTitle = string.Empty;

    [ObservableProperty]
    private KeyCategory _editCategory = KeyCategory.Account;

    [ObservableProperty]
    private string _editCustomIconGlyph = string.Empty;

    [ObservableProperty]
    private string _editPreviewGlyph = "\uE77B";

    [ObservableProperty]
    private string _editUsername = string.Empty;

    [ObservableProperty]
    private string _editSecret = string.Empty;

    [ObservableProperty]
    private string _editTargetApp = string.Empty;

    [ObservableProperty]
    private DateTimeOffset? _editExpirationDate;

    [ObservableProperty]
    private bool _editHasExpiration;

    [ObservableProperty]
    private string _editNotes = string.Empty;

    [ObservableProperty]
    private string _secretStrengthLabel = "Пусто";

    [ObservableProperty]
    private string _secretStrengthColor = "#7A7A7A";

    [ObservableProperty]
    private int _secretStrengthScore = 0;

    // Настраиваемый генератор паролей и ключей (с параметрами)
    [ObservableProperty]
    private int _genPasswordLength = 20;

    [ObservableProperty]
    private bool _genUseUpper = true;

    [ObservableProperty]
    private bool _genUseLower = true;

    [ObservableProperty]
    private bool _genUseDigits = true;

    [ObservableProperty]
    private bool _genUseSpecial = true;

    [ObservableProperty]
    private bool _genExcludeAmbiguous = true;

    // TOTP 2FA Authenticator + Живая проверка прохождения 2FA в окне редактирования
    [ObservableProperty]
    private string _editTotpSecret = string.Empty;

    [ObservableProperty]
    private bool _hasValidEditTotp;

    [ObservableProperty]
    private string _editTotpLiveCode = "--- ---";

    [ObservableProperty]
    private string _editTotpCountdownText = "30 сек.";

    [ObservableProperty]
    private double _editTotpProgressRatio = 1.0;

    // Палитра из 20 иконок для кастомного выбора пользователем
    public List<IconOptionItem> AvailableIcons { get; } = new()
    {
        new() { Glyph = "\uE77B", Name = "Аккаунт / Профиль" },
        new() { Glyph = "\uE774", Name = "Веб-сайт / Браузер" },
        new() { Glyph = "\uE8D7", Name = "Ключ / Пароль" },
        new() { Glyph = "\uE72E", Name = "Защита / 2FA Щит" },
        new() { Glyph = "\uE8A7", Name = "Лицензия ПО / Сертификат" },
        new() { Glyph = "\uE753", Name = "Облако / SaaS" },
        new() { Glyph = "\uEDA2", Name = "Сервер / Дата-центр" },
        new() { Glyph = "\uE943", Name = "API / Код разработчика" },
        new() { Glyph = "\uE756", Name = "Терминал / SSH / Программа" },
        new() { Glyph = "\uE7FC", Name = "Игра / Игровой лаунчер" },
        new() { Glyph = "\uE8C7", Name = "Банковская карта / Финансы" },
        new() { Glyph = "\uE701", Name = "Wi-Fi / Роутер" },
        new() { Glyph = "\uE8AC", Name = "Крипто / Сейф" },
        new() { Glyph = "\uE715", Name = "Почта / Email" },
        new() { Glyph = "\uE719", Name = "Магазин / Покупки" },
        new() { Glyph = "\uE8EA", Name = "Смартфон / Мобильное" },
        new() { Glyph = "\uE821", Name = "Работа / Корпоративное" },
        new() { Glyph = "\uE80F", Name = "Дом / Личное" },
        new() { Glyph = "\uE735", Name = "VIP / Избранное" },
        new() { Glyph = "\uE70B", Name = "Заметка / Документ" }
    };

    // Windows Hello Биометрия
    [ObservableProperty]
    private bool _isWindowsHelloAvailable;

    [ObservableProperty]
    private bool _isWindowsHelloEnrolled;

    [ObservableProperty]
    private bool _settingsWindowsHelloEnabled;

    // Have I Been Pwned аудит утечек
    [ObservableProperty]
    private bool _isAuditingPasswords;

    [ObservableProperty]
    private double _auditProgress;

    [ObservableProperty]
    private string _auditStatusText = "Проверить базу паролей на утечки";

    [ObservableProperty]
    private int _breachedPasswordsCount;

    // Диалог настроек
    [ObservableProperty]
    private bool _isSettingsDialogVisible;

    [ObservableProperty]
    private string _settingsCloudFolder = string.Empty;

    [ObservableProperty]
    private bool _settingsCloudEnabled;

    [ObservableProperty]
    private int _settingsAutoLockMinutes = 15;

    [ObservableProperty]
    private string _settingsLocalPath = string.Empty;

    // Диалог импорта
    [ObservableProperty]
    private bool _isImportDialogVisible;

    [ObservableProperty]
    private string _importSummaryText = string.Empty;

    // Настройки интерфейса STORM AURORA FLUID
    [ObservableProperty]
    private string _selectedTheme = "MIDNIGHT (Фиолетовая)";

    [ObservableProperty]
    private string _selectedAccent = "Violet (#8B5CF6)";

    [ObservableProperty]
    private string _selectedAccentHex = "#8B5CF6";

    [ObservableProperty]
    private string _selectedLanguage = "Русский (ru)";

    [ObservableProperty]
    private string _selectedFont = "Inter Variable";

    public List<string> AvailableThemes { get; } = ThemeService.Themes.Select(t => t.DisplayName).ToList();
    public List<string> AvailableAccents { get; } = ThemeService.Accents.Select(a => a.DisplayName).ToList();
    public List<string> AvailableLanguages { get; } = LocalizationService.Languages.Select(l => l.DisplayName).ToList();
    public List<string> AvailableFonts { get; } = ThemeService.Fonts.Select(f => f.DisplayName).ToList();

    // Динамические стили и кисти оформления
    [ObservableProperty] private SolidColorBrush _currentBackgroundBrush = new(ThemeService.ColorFromHex("#0D0B18"));
    [ObservableProperty] private SolidColorBrush _currentHeaderBrush = new(ThemeService.ColorFromHex("#141026"));
    [ObservableProperty] private SolidColorBrush _currentSurfaceBrush = new(ThemeService.ColorFromHex("#17132B"));
    [ObservableProperty] private SolidColorBrush _currentCardBrush = new(ThemeService.ColorFromHex("#1F193B"));
    [ObservableProperty] private SolidColorBrush _currentCardStrokeBrush = new(ThemeService.ColorFromHex("#372E63"));
    [ObservableProperty] private SolidColorBrush _currentTextPrimaryBrush = new(ThemeService.ColorFromHex("#FFFFFF"));
    [ObservableProperty] private SolidColorBrush _currentTextSecondaryBrush = new(ThemeService.ColorFromHex("#A79EC8"));
    [ObservableProperty] private SolidColorBrush _currentAccentBrush = new(ThemeService.ColorFromHex("#8B5CF6"));
    [ObservableProperty] private FontFamily _currentFontFamily = new("Inter Variable, Segoe UI Variable Text, Segoe UI");

    // Динамические локализованные строки (11 языков)
    [ObservableProperty] private string _uiTitle = "STORM KEYS";
    [ObservableProperty] private string _uiSubtitle = "Менеджер ключей, паролей и лицензий";
    [ObservableProperty] private string _uiSearchPlaceholder = "Поиск по названию, логину, сервису или заметкам...";
    [ObservableProperty] private string _uiAdd = "Добавить";
    [ObservableProperty] private string _uiImport = "Импорт";
    [ObservableProperty] private string _uiExport = "Экспорт";
    [ObservableProperty] private string _uiSettings = "Настройки";
    [ObservableProperty] private string _uiLock = "Заблокировать";
    [ObservableProperty] private string _uiTotalKeys = "Всего ключей и паролей";
    [ObservableProperty] private string _uiExpiringSoon = "Истекают скоро (≤30 дн.)";
    [ObservableProperty] private string _uiExpiredLicenses = "Истёкшие лицензии";
    [ObservableProperty] private string _uiSyncStatus = "Статус синхронизации";
    [ObservableProperty] private string _uiCategoryAll = "Все категории";
    [ObservableProperty] private string _uiExpirationAll = "Все сроки";
    [ObservableProperty] private string _uiSettingsTitle = "Настройки STORM KEYS";
    [ObservableProperty] private string _uiSettingsLocalVault = "Локальное зашифрованное хранилище";
    [ObservableProperty] private string _uiSettingsCloudSync = "Синхронизация с облачным диском (OneDrive, Google Drive, WebDAV)";
    [ObservableProperty] private string _uiSettingsTheme = "Тема оформления (9 тем)";
    [ObservableProperty] private string _uiSettingsAccent = "Цвет акцента (13 акцентов)";
    [ObservableProperty] private string _uiSettingsLanguage = "Язык интерфейса (11 языков)";
    [ObservableProperty] private string _uiSettingsFont = "Шрифт (10 шрифтов)";
    [ObservableProperty] private string _uiSettingsAutoLock = "Автоблокировка при бездействии";
    [ObservableProperty] private string _uiSettingsSave = "Сохранить";
    [ObservableProperty] private string _uiSettingsClose = "Закрыть";
    [ObservableProperty] private string _uiCheckUpdates = "Проверить";
    [ObservableProperty] private string _uiInstallUpdate = "Обновить";
    [ObservableProperty] private string _uiPlatformCert = "STORM INTEGRATED PLATFORM";
    [ObservableProperty] private string _uiPlatformCertDesc = "Шифрование AES-256-GCM • Хеширование Argon2id • Экосистема STORM";
    [ObservableProperty] private string _uiUpdatesHeader = "Обновления STORM KEYS";
    [ObservableProperty] private string _uiTooltipAdd = "Добавить новый ключ или пароль (Ctrl+N)";
    [ObservableProperty] private string _uiTooltipImport = "Импорт ключей и паролей";
    [ObservableProperty] private string _uiTooltipExport = "Экспорт хранилища";
    [ObservableProperty] private string _uiTooltipSettings = "Настройки хранилища и облака";
    [ObservableProperty] private string _uiTooltipLock = "Заблокировать хранилище (Ctrl+L)";
    [ObservableProperty] private string _uiTooltipCopyUsername = "Скопировать логин";
    [ObservableProperty] private string _uiTooltipCopyPassword = "Скопировать пароль/ключ (буфер очистится автоматически)";
    [ObservableProperty] private string _uiTooltipCardDetails = "Стилизованная подсказка и детали";
    [ObservableProperty] private string _uiTooltipEdit = "Редактировать";
    [ObservableProperty] private string _uiTooltipDelete = "Удалить";
    [ObservableProperty] private string _uiTooltipGenPassword = "Сгенерировать надежный пароль по выбранным параметрам";
    [ObservableProperty] private string _uiTooltipGenSoftwareKey = "Сгенерировать формат ключа ПО (XXXXX-XXXXX...)";
    [ObservableProperty] private string _uiAboutTitle = "О программе STORM KEYS";
    [ObservableProperty] private string _uiAboutPublisher = "Проверенный издатель: STORM TEAM";
    [ObservableProperty] private string _uiAboutVersion = $"Версия {UpdateService.CurrentVersionString}";
    [ObservableProperty] private string _uiAboutCert = "Сертификат: STORM INTEGRATED PLATFORM";
    [ObservableProperty] private string _uiAboutDesc = "Защищённое хранилище цифровых ключей, паролей и лицензий с шифрованием AES-256-GCM и деривацией Argon2id.";
    [ObservableProperty] private string _uiAboutClose = "Закрыть";
    [ObservableProperty] private string _uiWindowsHelloUnlock = "Войти через Windows Hello";
    [ObservableProperty] private string _uiWindowsHelloToggle = "Биометрия Windows Hello (отпечаток / лицо / PIN)";
    [ObservableProperty] private string _uiTotpLabel = "Код 2FA (TOTP)";
    [ObservableProperty] private string _uiTotpCopyTooltip = "Скопировать код 2FA";
    [ObservableProperty] private string _uiTotpSecretLabel = "Секретный ключ 2FA (Base32 или otpauth://)";
    [ObservableProperty] private string _uiTotpGenerate = "Сгенерировать ключ 2FA";
    [ObservableProperty] private string _uiAuditPasswords = "Проверить базу паролей на утечки";

    // Стилизованная подсказка (TeachingTip)
    [ObservableProperty]
    private bool _isTeachingTipOpen;

    [ObservableProperty]
    private string _teachingTipTitle = string.Empty;

    [ObservableProperty]
    private string _teachingTipSubtitle = string.Empty;

    [ObservableProperty]
    private string _teachingTipSecret = string.Empty;

    [ObservableProperty]
    private string _teachingTipStatus = string.Empty;

    [ObservableProperty]
    private string _teachingTipBadgeColor = "#107C41";

    public MainViewModel(
        IVaultService vaultService,
        ICryptoService cryptoService,
        IImportExportService importExportService,
        INotificationService notificationService,
        IStormCertificateService certService,
        IUpdateService updateService,
        ITotpService totpService,
        IWindowsHelloService windowsHelloService,
        IPwnedPasswordsService pwnedPasswordsService)
    {
        _vaultService = vaultService;
        _cryptoService = cryptoService;
        _importExportService = importExportService;
        _notificationService = notificationService;
        _certService = certService;
        _updateService = updateService;
        _totpService = totpService;
        _windowsHelloService = windowsHelloService;
        _pwnedPasswordsService = pwnedPasswordsService;
    }

    public void Initialize(DispatcherQueue dispatcherQueue)
    {
        _dispatcherQueue = dispatcherQueue;

        var cert = _certService.CheckStatus();
        CertificateStatus = cert.StatusText;

        IsVaultUninitialized = !_vaultService.IsVaultCreated;
        IsVaultLocked = true;

        SettingsLocalPath = _vaultService.LocalVaultPath;
        SettingsCloudFolder = _vaultService.Settings.CloudSyncFolderPath;
        SettingsCloudEnabled = _vaultService.Settings.CloudSyncEnabled;
        SettingsAutoLockMinutes = _vaultService.Settings.AutoLockMinutes;
        SettingsWindowsHelloEnabled = _vaultService.Settings.WindowsHelloEnabled && _windowsHelloService.IsEnrolled();
        var savedTheme = _vaultService.Settings.Theme ?? "MIDNIGHT";
        SelectedTheme = AvailableThemes.FirstOrDefault(t => t.StartsWith(savedTheme, StringComparison.OrdinalIgnoreCase)) ?? AvailableThemes[0];

        var savedAccent = _vaultService.Settings.Accent ?? "Violet";
        SelectedAccent = AvailableAccents.FirstOrDefault(a => a.StartsWith(savedAccent, StringComparison.OrdinalIgnoreCase)) ?? AvailableAccents[0];

        var savedLang = string.IsNullOrEmpty(_vaultService.Settings.Language) ? LocalizationService.GetSystemLanguageCode() : _vaultService.Settings.Language;
        SelectedLanguage = AvailableLanguages.FirstOrDefault(l => l.Contains($"({savedLang})", StringComparison.OrdinalIgnoreCase)) ?? AvailableLanguages[0];

        var savedFont = _vaultService.Settings.FontFamily ?? "Inter Variable";
        SelectedFont = AvailableFonts.FirstOrDefault(f => f.StartsWith(savedFont, StringComparison.OrdinalIgnoreCase)) ?? AvailableFonts[0];

        IsUpdateAvailable = false;
        UpdateStatusText = $"Установлена актуальная версия ({UpdateService.CurrentVersionString})";

        ApplyCurrentTheme();
        ApplyCurrentLanguage(LocalizationService.GetLanguage(SelectedLanguage).Code);

        // Проверка биометрии Windows Hello
        _ = Task.Run(async () =>
        {
            try
            {
                bool supported = await _windowsHelloService.IsSupportedAsync();
                bool enrolled = _windowsHelloService.IsEnrolled();
                _dispatcherQueue?.TryEnqueue(() =>
                {
                    IsWindowsHelloAvailable = supported;
                    IsWindowsHelloEnrolled = enrolled;
                    SettingsWindowsHelloEnabled = _vaultService.Settings.WindowsHelloEnabled && enrolled;
                });
            }
            catch { }
        });

        // Запуск таймера обновления кодов TOTP 2FA (включая живое превью в диалоге создания/редактирования)
        _totpTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _totpTimer.Tick += (s, e) => UpdateTotpCodes();
        _totpTimer.Start();

        _notificationService.OnInAppNotification += (title, msg, entry) =>
        {
            _dispatcherQueue.TryEnqueue(() =>
            {
                StatusMessage = $"{title}: {msg}";
            });
        };

        UpdateCloudStatus();

        // Тихая фоновая проверка обновлений по стандарту экосистемы STORM (раздел 9)
        _ = Task.Run(async () =>
        {
            try
            {
                var update = await _updateService.CheckForUpdatesAsync(silent: true);
                _dispatcherQueue.TryEnqueue(() =>
                {
                    if (update != null && UpdateService.IsNewerVersion(update.Version, UpdateService.CurrentVersion))
                    {
                        IsUpdateAvailable = true;
                        UpdateVersion = update.Version;
                        UpdateChangelog = update.Changelog;
                        UpdateDownloadUrl = update.DownloadUrl;
                        UpdateFileName = update.FileName;
                        UpdateStatusText = $"Доступно обновление: {update.Version}";
                        StatusMessage = $"Доступна новая версия STORM KEYS: {update.Version}";
                    }
                    else
                    {
                        IsUpdateAvailable = false;
                        UpdateStatusText = $"Установлена актуальная версия ({UpdateService.CurrentVersionString})";
                    }
                });
            }
            catch { }
        });
    }

    private void UpdateCloudStatus()
    {
        if (_vaultService.Settings.CloudSyncEnabled && !string.IsNullOrWhiteSpace(_vaultService.Settings.CloudSyncFolderPath))
        {
            IsCloudSyncConfigured = true;
            string folderName = Path.GetFileName(_vaultService.Settings.CloudSyncFolderPath);
            if (string.IsNullOrEmpty(folderName))
                folderName = _vaultService.Settings.CloudSyncFolderPath;

            CloudSyncStatusText = $"Облако: {folderName}";
        }
        else
        {
            IsCloudSyncConfigured = false;
            CloudSyncStatusText = "Локально (STORM VAULT)";
        }
    }

    partial void OnSearchTextChanged(string value) => RefreshFilteredList();
    partial void OnSelectedCategoryFilterChanged(string value) => RefreshFilteredList();
    partial void OnSelectedExpirationFilterChanged(string value) => RefreshFilteredList();
    partial void OnSelectedSortOrderChanged(string value) => RefreshFilteredList();

    partial void OnIsCompactViewChanged(bool value)
    {
        CardPadding = value ? new Thickness(12, 6, 12, 6) : new Thickness(16, 12, 16, 12);
    }

    partial void OnEditCategoryChanged(KeyCategory value)
    {
        UpdateEditPreviewGlyph();
    }

    partial void OnEditCustomIconGlyphChanged(string value)
    {
        UpdateEditPreviewGlyph();
    }

    private void UpdateEditPreviewGlyph()
    {
        if (!string.IsNullOrWhiteSpace(EditCustomIconGlyph))
        {
            EditPreviewGlyph = EditCustomIconGlyph;
            return;
        }

        EditPreviewGlyph = EditCategory switch
        {
            KeyCategory.Account => "\uE77B",
            KeyCategory.License => "\uE8A7",
            KeyCategory.ApiKey => "\uE943",
            KeyCategory.Certificate => "\uE72E",
            KeyCategory.Server => "\uEDA2",
            KeyCategory.BankCard => "\uE8C7",
            KeyCategory.Wifi => "\uE701",
            KeyCategory.Crypto => "\uE8AC",
            _ => "\uE8D7"
        };
    }

    partial void OnEditTargetAppChanged(string value)
    {
        if (!IsNewEntry || string.IsNullOrWhiteSpace(value)) return;
        string v = value.Trim().ToLowerInvariant();
        if (v.StartsWith("ssh://") || v.StartsWith("rdp://") || v.StartsWith("postgres://") || v.StartsWith("mysql://") || v.EndsWith(".rdp"))
            EditCategory = KeyCategory.Server;
        else if (v.Contains("api.") || v.Contains("/api/") || v.Contains("token"))
            EditCategory = KeyCategory.ApiKey;

        // Автоподстановка названия из домена или .exe, если название ещё пустое
        if (string.IsNullOrWhiteSpace(EditTitle))
        {
            if (v.EndsWith(".exe") || v.EndsWith(".lnk") || v.EndsWith(".rdp"))
            {
                try { EditTitle = Path.GetFileNameWithoutExtension(value.Trim()); } catch { }
            }
            else if (Uri.TryCreate(value.Trim().StartsWith("http") ? value.Trim() : "https://" + value.Trim(), UriKind.Absolute, out var u) && !string.IsNullOrEmpty(u.Host))
            {
                string host = u.Host.StartsWith("www.") ? u.Host[4..] : u.Host;
                EditTitle = host;
            }
        }
    }

    partial void OnEditSecretChanged(string value)
    {
        var eval = PasswordGenerator.EvaluateStrength(value);
        SecretStrengthScore = eval.Score;
        SecretStrengthLabel = eval.Label;
        SecretStrengthColor = eval.ColorHex;
        double bits = PasswordGenerator.CalculateEntropyBits(value);
        SecretEntropyText = $"{bits:F1} бит";
        SecretCrackTimeText = PasswordGenerator.EstimateCrackTime(value);

        // Умное автоопределение категории для лицензионных ключей XXXXX-XXXXX-...
        if (IsNewEntry && !string.IsNullOrWhiteSpace(value) && value.Length >= 19 && value.Split('-').Length >= 4 && EditCategory == KeyCategory.Account)
        {
            EditCategory = KeyCategory.License;
        }
    }

    partial void OnEditTotpSecretChanged(string value)
    {
        if (!string.IsNullOrWhiteSpace(value) && value.Trim().StartsWith("otpauth://", StringComparison.OrdinalIgnoreCase))
        {
            if (_totpService.TryParseOtpAuthInput(value, out string cleanSecret, out string? issuer, out string? account))
            {
                if (string.IsNullOrWhiteSpace(EditTitle) && !string.IsNullOrWhiteSpace(issuer))
                    EditTitle = issuer;
                if (string.IsNullOrWhiteSpace(EditUsername) && !string.IsNullOrWhiteSpace(account))
                    EditUsername = account;

                EditTotpSecret = cleanSecret;
                StatusMessage = "Ссылка otpauth:// распознана: секретный ключ 2FA и аккаунт подставлены!";
                return;
            }
        }

        UpdateEditDialogTotpPreview();
    }

    private void UpdateEditDialogTotpPreview()
    {
        if (string.IsNullOrWhiteSpace(EditTotpSecret))
        {
            HasValidEditTotp = false;
            EditTotpLiveCode = "--- ---";
            EditTotpCountdownText = "—";
            EditTotpProgressRatio = 0;
            return;
        }

        if (_totpService.TryParseOtpAuthInput(EditTotpSecret, out string cleanSecret, out _, out _))
        {
            string code = _totpService.GenerateCode(cleanSecret);
            if (!string.IsNullOrEmpty(code))
            {
                int sec = _totpService.GetSecondsRemaining();
                HasValidEditTotp = true;
                EditTotpLiveCode = _totpService.FormatCode(code);
                EditTotpCountdownText = $"Обновление через {sec} сек.";
                EditTotpProgressRatio = (double)sec / 30.0;
                return;
            }
        }

        HasValidEditTotp = false;
        EditTotpLiveCode = "Неверный Base32";
        EditTotpCountdownText = "Проверьте ключ";
        EditTotpProgressRatio = 0;
    }

    partial void OnEditHasExpirationChanged(bool value)
    {
        if (value && !_editExpirationDate.HasValue)
        {
            EditExpirationDate = DateTimeOffset.Now.AddYears(1);
        }
        else if (!value)
        {
            EditExpirationDate = null;
        }
    }

    public void UpdateTotpCodes()
    {
        if (IsEditDialogVisible && !string.IsNullOrWhiteSpace(EditTotpSecret))
        {
            UpdateEditDialogTotpPreview();
        }

        if (IsVaultLocked || _vaultService.CurrentContainer == null) return;

        int secRemaining = _totpService.GetSecondsRemaining();
        double progress = (double)secRemaining / 30.0;

        foreach (var entry in FilteredEntries)
        {
            if (entry.HasTotp && !string.IsNullOrWhiteSpace(entry.TotpSecret))
            {
                entry.CurrentTotpCode = _totpService.GenerateCode(entry.TotpSecret);
                entry.TotpSecondsRemaining = secRemaining;
                entry.TotpProgress = progress;
            }
        }

        // Автоблокировка при неактивности пользователя
        if (!IsVaultLocked && _vaultService.Settings.AutoLockMinutes > 0)
        {
            var idleMinutes = (DateTimeOffset.Now - _lastActivityTime).TotalMinutes;
            if (idleMinutes >= _vaultService.Settings.AutoLockMinutes)
            {
                _ = LockVaultAsync();
                StatusMessage = "Хранилище автоматически заблокировано по таймеру неактивности.";
            }
        }
    }

    [RelayCommand]
    public async Task UnlockWithWindowsHelloAsync()
    {
        if (IsLoading) return;
        IsLoading = true;
        MasterPasswordErrorMessage = string.Empty;
        try
        {
            string? masterPassword = await _windowsHelloService.UnlockWithHelloAsync();
            if (string.IsNullOrEmpty(masterPassword))
            {
                MasterPasswordErrorMessage = "Биометрическая аутентификация Windows Hello отменена или не пройдена.";
                return;
            }

            bool ok = await _vaultService.UnlockVaultAsync(masterPassword);
            if (ok)
            {
                IsVaultLocked = false;
                MasterPasswordInput = string.Empty;
                ConfirmPasswordInput = string.Empty;
                RefreshFilteredList();
                UpdateTotpCodes();

                if (_dispatcherQueue != null)
                {
                    _notificationService.StartPeriodicCheck(() => _vaultService.CurrentContainer?.Entries ?? Enumerable.Empty<KeyEntry>(), _dispatcherQueue);
                }

                StatusMessage = "Хранилище успешно разблокировано через Windows Hello!";
            }
            else
            {
                MasterPasswordErrorMessage = "Сохранённый ключ Windows Hello не подошёл к хранилищу.";
            }
        }
        catch (Exception ex)
        {
            MasterPasswordErrorMessage = $"Ошибка Windows Hello: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public async Task UnlockOrCreateVaultAsync()
    {
        MasterPasswordErrorMessage = string.Empty;
        if (string.IsNullOrWhiteSpace(MasterPasswordInput))
        {
            MasterPasswordErrorMessage = "Введите мастер-пароль.";
            return;
        }

        IsLoading = true;
        try
        {
            if (IsVaultUninitialized)
            {
                if (MasterPasswordInput.Length < 6)
                {
                    MasterPasswordErrorMessage = "Мастер-пароль должен содержать минимум 6 символов.";
                    return;
                }

                if (MasterPasswordInput != ConfirmPasswordInput)
                {
                    MasterPasswordErrorMessage = "Пароли не совпадают.";
                    return;
                }

                string pwd = MasterPasswordInput;
                await _vaultService.CreateVaultAsync(pwd);
                IsVaultUninitialized = false;
                IsVaultLocked = false;
                MasterPasswordInput = string.Empty;
                ConfirmPasswordInput = string.Empty;
                RefreshFilteredList();
                UpdateTotpCodes();

                if (SettingsWindowsHelloEnabled && !IsWindowsHelloEnrolled)
                {
                    _ = _windowsHelloService.EnrollAsync(pwd);
                }

                if (_dispatcherQueue != null)
                {
                    _notificationService.StartPeriodicCheck(() => _vaultService.CurrentContainer?.Entries ?? Enumerable.Empty<KeyEntry>(), _dispatcherQueue);
                }

                StatusMessage = "Новое зашифрованное хранилище успешно создано!";
            }
            else
            {
                string pwd = MasterPasswordInput;
                bool success = await _vaultService.UnlockVaultAsync(pwd);
                if (success)
                {
                    IsVaultLocked = false;
                    MasterPasswordInput = string.Empty;
                    ConfirmPasswordInput = string.Empty;
                    RefreshFilteredList();
                    UpdateTotpCodes();

                    if (SettingsWindowsHelloEnabled && !IsWindowsHelloEnrolled)
                    {
                        _ = _windowsHelloService.EnrollAsync(pwd);
                    }

                    if (_dispatcherQueue != null)
                    {
                        _notificationService.StartPeriodicCheck(() => _vaultService.CurrentContainer?.Entries ?? Enumerable.Empty<KeyEntry>(), _dispatcherQueue);
                    }

                    StatusMessage = "Хранилище успешно разблокировано.";
                }
                else
                {
                    MasterPasswordErrorMessage = "Неверный мастер-пароль. Доступ заблокирован.";
                }
            }
        }
        catch (Exception ex)
        {
            MasterPasswordErrorMessage = $"Ошибка: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public async Task LockVaultAsync()
    {
        await _vaultService.LockVaultAsync();
        IsVaultLocked = true;
        MasterPasswordInput = string.Empty;
        ConfirmPasswordInput = string.Empty;
        FilteredEntries.Clear();
        SelectedEntry = null;
        StatusMessage = "Хранилище защищено и заблокировано.";
    }

    public void RefreshFilteredList()
    {
        if (_vaultService.CurrentContainer == null)
            return;

        var allEntries = _vaultService.CurrentContainer.Entries;

        TotalEntriesCount = allEntries.Count;
        IsVaultEmpty = TotalEntriesCount == 0;
        ExpiringSoonCount = allEntries.Count(e => e.IsExpiringSoon);
        ExpiredCount = allEntries.Count(e => e.IsExpired);
        ActiveCount = allEntries.Count(e => !e.IsExpired);
        FavoritesCount = allEntries.Count(e => e.IsFavorite);
        BreachedCount = allEntries.Count(e => e.IsPwnedBreached);
        WeakPasswordsCount = allEntries.Count(e => !string.IsNullOrEmpty(e.SecretValue) && PasswordGenerator.EvaluateStrength(e.SecretValue).Score <= 2);

        var dupGroups = allEntries.Where(e => !string.IsNullOrEmpty(e.SecretValue))
                                  .GroupBy(e => e.SecretValue)
                                  .Where(g => g.Count() > 1);
        DuplicatePasswordsCount = dupGroups.Sum(g => g.Count());

        double total = Math.Max(1, TotalEntriesCount);
        ActiveRatio = (double)ActiveCount / total;
        ExpiringRatio = (double)ExpiringSoonCount / total;
        ExpiredRatio = (double)ExpiredCount / total;
        HealthScoreText = TotalEntriesCount == 0 ? "100%" : $"{(double)ActiveCount / total * 100:F0}%";

        IEnumerable<KeyEntry> query = allEntries;

        var duplicateSecrets = new HashSet<string>(
            allEntries.Where(e => !string.IsNullOrEmpty(e.SecretValue))
                      .GroupBy(e => e.SecretValue)
                      .Where(g => g.Count() > 1)
                      .Select(g => g.Key));

        // Умный многословный поиск (Multi-Token Smart Search) по всем полям, тегам и атрибутам
        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            var tokens = SearchText.Trim().ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            query = query.Where(e =>
            {
                string tagsJoined = e.Tags != null ? string.Join(" ", e.Tags).ToLowerInvariant() : string.Empty;
                string customJoined = e.CustomFields != null
                    ? string.Join(" ", e.CustomFields.Select(kv => $"{kv.Key}:{kv.Value}")).ToLowerInvariant()
                    : string.Empty;
                return tokens.All(t =>
                {
                    string cleanToken = t.TrimStart('#');
                    return e.Title.ToLowerInvariant().Contains(cleanToken) ||
                           e.UsernameOrEmail.ToLowerInvariant().Contains(cleanToken) ||
                           e.TargetAppOrUrl.ToLowerInvariant().Contains(cleanToken) ||
                           e.Notes.ToLowerInvariant().Contains(cleanToken) ||
                           e.CategoryDisplayName.ToLowerInvariant().Contains(cleanToken) ||
                           tagsJoined.Contains(cleanToken) ||
                           customJoined.Contains(cleanToken);
                });
            });
        }

        // Фильтрация по быстрым чипам (включая Слабые и Дубликаты)
        if (!string.IsNullOrEmpty(ActiveFilterChip) && ActiveFilterChip != "All")
        {
            query = ActiveFilterChip switch
            {
                "Favorites" => query.Where(e => e.IsFavorite),
                "Breached" => query.Where(e => e.IsPwnedBreached),
                "Weak" => query.Where(e => !string.IsNullOrEmpty(e.SecretValue) && PasswordGenerator.EvaluateStrength(e.SecretValue).Score <= 2),
                "Duplicates" => query.Where(e => !string.IsNullOrEmpty(e.SecretValue) && duplicateSecrets.Contains(e.SecretValue)),
                "Expiring" => query.Where(e => e.IsExpiringSoon || e.IsExpired),
                "Account" => query.Where(e => e.Category == KeyCategory.Account),
                "License" => query.Where(e => e.Category == KeyCategory.License),
                "ApiKey" => query.Where(e => e.Category == KeyCategory.ApiKey),
                "Server" => query.Where(e => e.Category == KeyCategory.Server),
                "BankCard" => query.Where(e => e.Category == KeyCategory.BankCard),
                "Wifi" => query.Where(e => e.Category == KeyCategory.Wifi),
                "Crypto" => query.Where(e => e.Category == KeyCategory.Crypto),
                _ => query
            };
        }

        // Фильтр категорий
        if (SelectedCategoryFilter != "Все категории")
        {
            query = SelectedCategoryFilter switch
            {
                "Учетные записи" => query.Where(e => e.Category == KeyCategory.Account),
                "Лицензии ПО" => query.Where(e => e.Category == KeyCategory.License),
                "API Ключи" => query.Where(e => e.Category == KeyCategory.ApiKey),
                "Сертификаты / SSH" => query.Where(e => e.Category == KeyCategory.Certificate),
                "Серверы" => query.Where(e => e.Category == KeyCategory.Server),
                "Банковские карты" => query.Where(e => e.Category == KeyCategory.BankCard),
                "Сети Wi-Fi" => query.Where(e => e.Category == KeyCategory.Wifi),
                "Крипто / Seed" => query.Where(e => e.Category == KeyCategory.Crypto),
                "Другое" => query.Where(e => e.Category == KeyCategory.Other),
                _ => query
            };
        }

        // Фильтр сроков окончания
        if (SelectedExpirationFilter != "Все сроки")
        {
            query = SelectedExpirationFilter switch
            {
                "Истекающие скоро (<=30 дн.)" => query.Where(e => e.IsExpiringSoon),
                "Критические (<=7 дн.)" => query.Where(e => e.IsCriticalExpiring),
                "Истёкшие" => query.Where(e => e.IsExpired),
                "Активные / Бессрочные" => query.Where(e => !e.IsExpired),
                _ => query
            };
        }

        // Многорежимная сортировка (Избранные всегда сверху)
        IEnumerable<KeyEntry> sorted = SelectedSortOrder switch
        {
            "По алфавиту (А–Я)" => query.OrderByDescending(e => e.IsFavorite).ThenBy(e => e.Title),
            "Сначала новые" => query.OrderByDescending(e => e.IsFavorite).ThenByDescending(e => e.ModifiedAt),
            "По надёжности" => query.OrderByDescending(e => e.IsFavorite).ThenBy(e => e.EntropyBits).ThenBy(e => e.Title),
            "По категории" => query.OrderByDescending(e => e.IsFavorite).ThenBy(e => e.Category).ThenBy(e => e.Title),
            _ => query.OrderByDescending(e => e.IsFavorite)
                      .ThenBy(e => e.IsExpired ? 0 : (e.IsCriticalExpiring ? 1 : (e.IsExpiringSoon ? 2 : 3)))
                      .ThenBy(e => e.DaysRemaining ?? int.MaxValue)
                      .ThenBy(e => e.Title)
        };
        var list = sorted.ToList();

        FilteredEntries.Clear();
        foreach (var item in list)
            FilteredEntries.Add(item);

        UpdateTotpCodes();
    }

    [RelayCommand]
    public void OpenAddDialog()
    {
        IsNewEntry = true;
        EditDialogHeader = "Новая запись (все форматы, иконки, генератор, 2FA)";
        EditTitle = string.Empty;
        EditCategory = KeyCategory.Account;
        EditCustomIconGlyph = string.Empty;
        EditUsername = string.Empty;
        EditSecret = string.Empty;
        EditTargetApp = string.Empty;
        EditExpirationDate = null;
        EditHasExpiration = false;
        EditNotes = string.Empty;
        EditTotpSecret = string.Empty;
        EditTagsText = string.Empty;
        EditCustomFieldsText = string.Empty;
        HasPasswordHistory = false;
        EditPasswordHistoryText = string.Empty;
        UpdateEditPreviewGlyph();
        UpdateEditDialogTotpPreview();
        IsEditDialogVisible = true;
    }

    [RelayCommand]
    public void OpenAddDialogWithTemplate(string templateKey)
    {
        OpenAddDialog();
        ApplyEntryTemplate(templateKey);
    }

    [RelayCommand]
    public void ApplyEntryTemplate(string templateKey)
    {
        switch (templateKey)
        {
            case "Account":
                EditCategory = KeyCategory.Account;
                EditCustomIconGlyph = "\uE77B";
                if (string.IsNullOrWhiteSpace(EditTagsText)) EditTagsText = "аккаунт, вход";
                break;
            case "License":
                EditCategory = KeyCategory.License;
                EditCustomIconGlyph = "\uE8A7";
                if (string.IsNullOrWhiteSpace(EditSecret)) EditSecret = PasswordGenerator.GenerateLicenseKey();
                if (string.IsNullOrWhiteSpace(EditTagsText)) EditTagsText = "лицензия, софт";
                if (string.IsNullOrWhiteSpace(EditCustomFieldsText)) EditCustomFieldsText = "Платформа: Windows / Steam\nКоличество ПК: 1";
                break;
            case "ApiKey":
                EditCategory = KeyCategory.ApiKey;
                EditCustomIconGlyph = "\uE943";
                if (string.IsNullOrWhiteSpace(EditTagsText)) EditTagsText = "api, token, dev";
                if (string.IsNullOrWhiteSpace(EditCustomFieldsText)) EditCustomFieldsText = "Тип: Bearer Token\nОкружение: Production";
                break;
            case "Certificate":
                EditCategory = KeyCategory.Certificate;
                EditCustomIconGlyph = "\uE72E";
                if (string.IsNullOrWhiteSpace(EditTagsText)) EditTagsText = "ssh, ssl, cert";
                if (string.IsNullOrWhiteSpace(EditCustomFieldsText)) EditCustomFieldsText = "Алгоритм: Ed25519 / RSA-4096\nОтпечаток: SHA256";
                break;
            case "Server":
                EditCategory = KeyCategory.Server;
                EditCustomIconGlyph = "\uEDA2";
                if (string.IsNullOrWhiteSpace(EditTagsText)) EditTagsText = "сервер, rdp, ssh";
                if (string.IsNullOrWhiteSpace(EditCustomFieldsText)) EditCustomFieldsText = "IP / Хост: 192.168.1.10\nПорт: 22\nПротокол: SSH / RDP";
                break;
            case "BankCard":
                EditCategory = KeyCategory.BankCard;
                EditCustomIconGlyph = "\uE8C7";
                if (string.IsNullOrWhiteSpace(EditTagsText)) EditTagsText = "карта, финансы";
                if (string.IsNullOrWhiteSpace(EditCustomFieldsText)) EditCustomFieldsText = "Срок (ММ/ГГ): 12/28\nCVV / CVC: 000\nPIN-код: 0000\nБанк: ";
                break;
            case "Wifi":
                EditCategory = KeyCategory.Wifi;
                EditCustomIconGlyph = "\uE701";
                if (string.IsNullOrWhiteSpace(EditTagsText)) EditTagsText = "wifi, сеть";
                if (string.IsNullOrWhiteSpace(EditCustomFieldsText)) EditCustomFieldsText = "SSID (Имя сети): \nЗащита: WPA2/WPA3\nIP роутера: 192.168.1.1";
                break;
            case "Crypto":
                EditCategory = KeyCategory.Crypto;
                EditCustomIconGlyph = "\uE8AC";
                if (string.IsNullOrWhiteSpace(EditTagsText)) EditTagsText = "крипто, seed, кошелек";
                if (string.IsNullOrWhiteSpace(EditCustomFieldsText)) EditCustomFieldsText = "Сеть: BTC / ETH / TON / USDT\nПубличный адрес: \nSeed-фраза (12/24 слова): ";
                break;
            case "Other":
                EditCategory = KeyCategory.Other;
                EditCustomIconGlyph = "\uE70B";
                if (string.IsNullOrWhiteSpace(EditTagsText)) EditTagsText = "заметка, pin";
                break;
        }
        UpdateEditPreviewGlyph();
    }

    [RelayCommand]
    public void SelectCustomIcon(string glyph)
    {
        if (!string.IsNullOrWhiteSpace(glyph))
        {
            EditCustomIconGlyph = glyph;
            UpdateEditPreviewGlyph();
        }
    }

    [RelayCommand]
    public async Task FillDialogFromClipboardAsync()
    {
        try
        {
            var dataPackageView = Windows.ApplicationModel.DataTransfer.Clipboard.GetContent();
            if (dataPackageView.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.Text))
            {
                string text = await dataPackageView.GetTextAsync();
                var res = _importExportService.ImportFromClipboardText(text);
                if (res.ImportedEntries.Count > 0)
                {
                    var item = res.ImportedEntries[0];
                    if (!string.IsNullOrWhiteSpace(item.Title)) EditTitle = item.Title;
                    EditCategory = item.Category;
                    if (!string.IsNullOrWhiteSpace(item.UsernameOrEmail)) EditUsername = item.UsernameOrEmail;
                    if (!string.IsNullOrWhiteSpace(item.SecretValue)) EditSecret = item.SecretValue;
                    if (!string.IsNullOrWhiteSpace(item.TotpSecret)) EditTotpSecret = item.TotpSecret;
                    if (!string.IsNullOrWhiteSpace(item.TargetAppOrUrl)) EditTargetApp = item.TargetAppOrUrl;
                    if (!string.IsNullOrWhiteSpace(item.Notes)) EditNotes = item.Notes;
                    StatusMessage = $"Поля формы автоматически заполнены из буфера ({res.FormatDetected})!";
                }
                else if (!string.IsNullOrWhiteSpace(text))
                {
                    EditSecret = text.Trim();
                    StatusMessage = "Текст из буфера вставлен в поле пароля/ключа.";
                }
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Ошибка чтения буфера: {ex.Message}";
        }
    }

    [RelayCommand]
    public void OpenEditDialog(KeyEntry? entry)
    {
        if (entry == null) return;
        SelectedEntry = entry;
        IsNewEntry = false;
        EditDialogHeader = $"Редактирование: {entry.Title}";
        EditTitle = entry.Title;
        EditCategory = entry.Category;
        EditCustomIconGlyph = entry.CustomIconGlyph ?? string.Empty;
        EditUsername = entry.UsernameOrEmail;
        EditSecret = entry.SecretValue;
        EditTargetApp = entry.TargetAppOrUrl;
        EditExpirationDate = entry.ExpirationDate;
        EditHasExpiration = entry.ExpirationDate.HasValue;
        EditNotes = entry.Notes;
        EditTotpSecret = entry.TotpSecret ?? string.Empty;
        EditTagsText = entry.Tags != null && entry.Tags.Count > 0 ? string.Join(", ", entry.Tags) : string.Empty;
        EditCustomFieldsText = entry.CustomFields != null && entry.CustomFields.Count > 0
            ? string.Join(Environment.NewLine, entry.CustomFields.Select(kv => $"{kv.Key}: {kv.Value}"))
            : string.Empty;
        HasPasswordHistory = entry.PasswordHistory != null && entry.PasswordHistory.Count > 0;
        EditPasswordHistoryText = HasPasswordHistory
            ? $"В истории {entry.PasswordHistory!.Count} пред. паролей (посл. от {entry.PasswordHistory[0].ChangedAt:dd.MM.yyyy HH:mm})"
            : string.Empty;
        UpdateEditPreviewGlyph();
        UpdateEditDialogTotpPreview();
        IsEditDialogVisible = true;
    }

    [RelayCommand]
    public async Task ToggleFavoriteAsync(KeyEntry? entry)
    {
        if (entry == null || _vaultService.CurrentContainer == null) return;
        RecordUserActivity();
        entry.IsFavorite = !entry.IsFavorite;
        await _vaultService.SaveVaultAsync();
        RefreshFilteredList();
        StatusMessage = entry.IsFavorite ? $"«{entry.Title}» добавлено в избранное!" : $"«{entry.Title}» удалено из избранного.";
    }

    [RelayCommand]
    public async Task ToggleDensityAsync()
    {
        RecordUserActivity();
        IsCompactView = !IsCompactView;
        _vaultService.Settings.IsCompactView = IsCompactView;
        await _vaultService.SaveSettingsAsync();
    }

    [RelayCommand]
    public void FilterByChip(string chip)
    {
        RecordUserActivity();
        ActiveFilterChip = chip;
        RefreshFilteredList();
    }

    [RelayCommand]
    public async Task LaunchUrlAsync(KeyEntry? entry)
    {
        if (entry == null || string.IsNullOrWhiteSpace(entry.TargetAppOrUrl))
            return;

        RecordUserActivity();
        string target = entry.TargetAppOrUrl.Trim();

        try
        {
            // 1. Запуск привязанной локальной программы (.exe, .lnk, .rdp, .bat)
            if (entry.IsAppBinding)
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = target,
                    UseShellExecute = true
                });
                StatusMessage = $"Запущена привязанная программа: {Path.GetFileName(target)}";
                return;
            }

            // 2. Открытие сайта или URI протокола (https://, steam://, ssh://, rdp://)
            if (entry.WebsiteLaunchUri != null)
            {
                await Windows.System.Launcher.LaunchUriAsync(entry.WebsiteLaunchUri);
                StatusMessage = $"Открыт сайт / ресурс: {entry.WebsiteLaunchUri}";
                return;
            }

            // 3. Попытка запуска через системный Shell
            Process.Start(new ProcessStartInfo
            {
                FileName = target,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            StatusMessage = $"Не удалось запустить «{target}»: {ex.Message}";
        }
    }

    [RelayCommand]
    public async Task AutoTypeEntryAsync(KeyEntry? entry)
    {
        if (entry == null) return;
        RecordUserActivity();

        // Если у записи настроен 2FA, сразу копируем текущий 6-значный код 2FA в буфер обмена для мгновенного прохождения 2FA
        if (entry.HasTotp && !string.IsNullOrWhiteSpace(entry.CurrentTotpCode) && _dispatcherQueue != null)
        {
            int timeout = _vaultService.Settings.ClearClipboardAfterSeconds ? _vaultService.Settings.ClipboardClearTimeout : 0;
            ClipboardHelper.CopyToClipboard(entry.CurrentTotpCode.Replace(" ", ""), _dispatcherQueue, timeout);
        }

        IntPtr hwnd = MainWindow.Instance != null ? WinRT.Interop.WindowNative.GetWindowHandle(MainWindow.Instance) : IntPtr.Zero;
        bool ok = await AutoTypeHelper.AutoTypeCredentialsAsync(hwnd, entry.UsernameOrEmail, entry.SecretValue, pressEnter: true);
        if (ok)
        {
            StatusMessage = entry.HasTotp
                ? $"Авто-ввод «{entry.Title}» выполнен! Код 2FA ({entry.CurrentTotpCode}) уже в буфере обмена (Ctrl+V)."
                : $"Быстрая авто-вставка «{entry.Title}» в активное окно выполнена!";
        }
    }

    [RelayCommand]
    public async Task AutoTypeTotpAsync(KeyEntry? entry)
    {
        if (entry == null || !entry.HasTotp) return;
        RecordUserActivity();
        string code = string.IsNullOrWhiteSpace(entry.CurrentTotpCode)
            ? _totpService.GenerateCode(entry.TotpSecret)
            : entry.CurrentTotpCode.Replace(" ", "");

        if (string.IsNullOrEmpty(code)) return;

        IntPtr hwnd = MainWindow.Instance != null ? WinRT.Interop.WindowNative.GetWindowHandle(MainWindow.Instance) : IntPtr.Zero;
        await AutoTypeHelper.AutoTypeTextAsync(hwnd, code, pressEnter: true);
        StatusMessage = $"Код 2FA ({code}) автоматически введён в активное окно!";
    }

    [RelayCommand]
    public void CopyEditTotpCode()
    {
        if (!HasValidEditTotp || _dispatcherQueue == null) return;
        string rawCode = EditTotpLiveCode.Replace(" ", "");
        int timeout = _vaultService.Settings.ClearClipboardAfterSeconds ? _vaultService.Settings.ClipboardClearTimeout : 0;
        ClipboardHelper.CopyToClipboard(rawCode, _dispatcherQueue, timeout);
        StatusMessage = $"Проверочный код 2FA «{EditTotpLiveCode}» скопирован для подтверждения на сайте!";
    }

    [RelayCommand]
    public void ToggleHealthDashboard()
    {
        RecordUserActivity();
        IsHealthDashboardVisible = !IsHealthDashboardVisible;
    }

    [RelayCommand]
    public async Task SaveEntryAsync()
    {
        if (string.IsNullOrWhiteSpace(EditTitle))
        {
            StatusMessage = "Укажите название записи.";
            return;
        }

        if (_vaultService.CurrentContainer == null) return;

        string cleanTotp = string.Empty;
        if (!string.IsNullOrWhiteSpace(EditTotpSecret))
        {
            if (_totpService.TryParseOtpAuthInput(EditTotpSecret, out string parsedSecret, out _, out _))
                cleanTotp = parsedSecret;
            else
                cleanTotp = EditTotpSecret.Trim().Replace(" ", "").Replace("-", "").ToUpperInvariant();
        }

        if (IsNewEntry)
        {
            var newEntry = new KeyEntry
            {
                Title = EditTitle.Trim(),
                Category = EditCategory,
                CustomIconGlyph = EditCustomIconGlyph,
                UsernameOrEmail = EditUsername.Trim(),
                SecretValue = EditSecret,
                TargetAppOrUrl = EditTargetApp.Trim(),
                ExpirationDate = EditHasExpiration ? EditExpirationDate : null,
                Notes = EditNotes.Trim(),
                TotpSecret = cleanTotp,
                Tags = ParseTags(EditTagsText),
                CustomFields = ParseCustomFields(EditCustomFieldsText),
                CreatedAt = DateTimeOffset.UtcNow,
                ModifiedAt = DateTimeOffset.UtcNow
            };
            if (newEntry.HasTotp && !string.IsNullOrWhiteSpace(newEntry.TotpSecret))
            {
                newEntry.CurrentTotpCode = _totpService.GenerateCode(newEntry.TotpSecret);
                newEntry.TotpSecondsRemaining = _totpService.GetSecondsRemaining();
                newEntry.TotpProgress = (double)newEntry.TotpSecondsRemaining / 30.0;
            }
            _vaultService.CurrentContainer.Entries.Add(newEntry);
            StatusMessage = $"Запись «{newEntry.Title}» сохранена.";
        }
        else if (SelectedEntry != null)
        {
            SelectedEntry.Title = EditTitle.Trim();
            SelectedEntry.Category = EditCategory;
            SelectedEntry.CustomIconGlyph = EditCustomIconGlyph;
            SelectedEntry.UsernameOrEmail = EditUsername.Trim();

            if (!IsNewEntry && SelectedEntry.SecretValue != EditSecret && !string.IsNullOrEmpty(SelectedEntry.SecretValue))
            {
                SelectedEntry.PasswordHistory ??= new();
                SelectedEntry.PasswordHistory.Insert(0, new PasswordHistoryItem
                {
                    Password = SelectedEntry.SecretValue,
                    ChangedAt = DateTimeOffset.Now
                });
                if (SelectedEntry.PasswordHistory.Count > 5)
                    SelectedEntry.PasswordHistory.RemoveAt(SelectedEntry.PasswordHistory.Count - 1);
            }

            SelectedEntry.SecretValue = EditSecret;
            SelectedEntry.TargetAppOrUrl = EditTargetApp.Trim();
            SelectedEntry.ExpirationDate = EditHasExpiration ? EditExpirationDate : null;
            SelectedEntry.Notes = EditNotes.Trim();
            SelectedEntry.TotpSecret = cleanTotp;
            SelectedEntry.Tags = ParseTags(EditTagsText);
            SelectedEntry.CustomFields = ParseCustomFields(EditCustomFieldsText);
            if (SelectedEntry.HasTotp && !string.IsNullOrWhiteSpace(SelectedEntry.TotpSecret))
            {
                SelectedEntry.CurrentTotpCode = _totpService.GenerateCode(SelectedEntry.TotpSecret);
                SelectedEntry.TotpSecondsRemaining = _totpService.GetSecondsRemaining();
                SelectedEntry.TotpProgress = (double)SelectedEntry.TotpSecondsRemaining / 30.0;
            }
            else
            {
                SelectedEntry.CurrentTotpCode = string.Empty;
            }
            SelectedEntry.ModifiedAt = DateTimeOffset.UtcNow;
            SelectedEntry.Notified30Days = false;
            SelectedEntry.Notified7Days = false;
            SelectedEntry.Notified1Day = false;
            SelectedEntry.NotifiedExpired = false;
            StatusMessage = $"Запись «{SelectedEntry.Title}» обновлена.";
        }

        IsEditDialogVisible = false;
        await _vaultService.SaveVaultAsync();
        RefreshFilteredList();
    }

    private static List<string> ParseTags(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return new List<string>();
        return raw.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                  .Select(t => t.Trim().TrimStart('#'))
                  .Where(t => !string.IsNullOrWhiteSpace(t))
                  .Distinct(StringComparer.OrdinalIgnoreCase)
                  .ToList();
    }

    private static Dictionary<string, string> ParseCustomFields(string? raw)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(raw)) return dict;
        var lines = raw.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            int idx = line.IndexOf(':');
            if (idx > 0)
            {
                string k = line[..idx].Trim();
                string v = line[(idx + 1)..].Trim();
                if (!string.IsNullOrEmpty(k)) dict[k] = v;
            }
        }
        return dict;
    }

    [RelayCommand]
    public async Task DeleteEntryAsync(KeyEntry? entry)
    {
        if (entry == null || _vaultService.CurrentContainer == null) return;

        _lastDeletedEntry = entry;
        CanUndoDelete = true;
        _vaultService.CurrentContainer.Entries.Remove(entry);
        await _vaultService.SaveVaultAsync();
        RefreshFilteredList();
        StatusMessage = $"Запись «{entry.Title}» удалена (Ctrl+Z — вернуть).";
    }

    [RelayCommand]
    public async Task UndoDeleteAsync()
    {
        if (_lastDeletedEntry == null || _vaultService.CurrentContainer == null) return;
        var restored = _lastDeletedEntry;
        _lastDeletedEntry = null;
        CanUndoDelete = false;
        _vaultService.CurrentContainer.Entries.Add(restored);
        await _vaultService.SaveVaultAsync();
        RefreshFilteredList();
        StatusMessage = $"Запись «{restored.Title}» восстановлена!";
    }

    [RelayCommand]
    public async Task DuplicateEntryAsync(KeyEntry? entry)
    {
        if (entry == null || _vaultService.CurrentContainer == null) return;
        RecordUserActivity();
        var clone = entry.Clone();
        _vaultService.CurrentContainer.Entries.Add(clone);
        await _vaultService.SaveVaultAsync();
        RefreshFilteredList();
        StatusMessage = $"Создана копия: «{clone.Title}».";
    }

    [RelayCommand]
    public void PeekSecret(KeyEntry? entry)
    {
        if (entry == null) return;
        RecordUserActivity();
        entry.IsSecretRevealed = !entry.IsSecretRevealed;
        if (entry.IsSecretRevealed)
        {
            _ = Task.Run(async () =>
            {
                await Task.Delay(5000);
                _dispatcherQueue?.TryEnqueue(() =>
                {
                    entry.IsSecretRevealed = false;
                });
            });
        }
    }

    [RelayCommand]
    public void CopyFullSummary(KeyEntry? entry)
    {
        if (entry == null || _dispatcherQueue == null) return;
        RecordUserActivity();
        var lines = new List<string> { $"[{entry.Title}] ({entry.CategoryDisplayName})" };
        if (!string.IsNullOrWhiteSpace(entry.UsernameOrEmail)) lines.Add($"Логин: {entry.UsernameOrEmail}");
        if (!string.IsNullOrWhiteSpace(entry.SecretValue)) lines.Add($"Пароль/Ключ: {entry.SecretValue}");
        if (entry.HasTotp && !string.IsNullOrWhiteSpace(entry.CurrentTotpCode)) lines.Add($"Код 2FA: {entry.CurrentTotpCode}");
        if (!string.IsNullOrWhiteSpace(entry.TargetAppOrUrl)) lines.Add($"URL/Программа: {entry.TargetAppOrUrl}");
        int timeout = _vaultService.Settings.ClearClipboardAfterSeconds ? _vaultService.Settings.ClipboardClearTimeout : 0;
        ClipboardHelper.CopyToClipboard(string.Join(Environment.NewLine, lines), _dispatcherQueue, timeout);
        StatusMessage = $"Полная сводка «{entry.Title}» скопирована в буфер!";
    }

    [RelayCommand]
    public void GeneratePassphrase()
    {
        EditSecret = PasswordGenerator.GeneratePassphrase(4, "-", true);
    }

    [RelayCommand]
    public void GeneratePinCode()
    {
        int pinLen = Math.Clamp(GenPasswordLength <= 12 ? GenPasswordLength : 6, 4, 12);
        EditSecret = PasswordGenerator.GeneratePin(pinLen);
    }

    [RelayCommand]
    public void SetGeneratorLengthPreset(string lenStr)
    {
        if (int.TryParse(lenStr, out int len))
        {
            GenPasswordLength = Math.Clamp(len, 6, 64);
            GeneratePassword();
        }
    }

    [RelayCommand]
    public void RestorePreviousPassword()
    {
        if (SelectedEntry?.PasswordHistory != null && SelectedEntry.PasswordHistory.Count > 0)
        {
            EditSecret = SelectedEntry.PasswordHistory[0].Password;
            StatusMessage = "Предыдущий пароль подставлен в поле ввода.";
        }
    }

    [RelayCommand]
    public async Task ChangeMasterPasswordAsync()
    {
        if (string.IsNullOrWhiteSpace(OldMasterPasswordInput) || string.IsNullOrWhiteSpace(NewMasterPasswordInput))
        {
            StatusMessage = "Укажите текущий и новый мастер-пароль.";
            return;
        }
        if (NewMasterPasswordInput.Length < 6)
        {
            StatusMessage = "Новый мастер-пароль должен содержать минимум 6 символов.";
            return;
        }
        try
        {
            await _vaultService.ChangeMasterPasswordAsync(OldMasterPasswordInput, NewMasterPasswordInput);
            if (SettingsWindowsHelloEnabled)
            {
                await _windowsHelloService.EnrollAsync(NewMasterPasswordInput);
            }
            OldMasterPasswordInput = string.Empty;
            NewMasterPasswordInput = string.Empty;
            StatusMessage = "Мастер-пароль хранилища успешно изменён и перешифрован!";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Ошибка смены мастер-пароля: {ex.Message}";
        }
    }

    [RelayCommand]
    public async Task CreateBackupSnapshotAsync()
    {
        try
        {
            string snap = await _vaultService.CreateBackupSnapshotAsync();
            StatusMessage = $"Резервная копия создана: {Path.GetFileName(snap)}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Ошибка резервного копирования: {ex.Message}";
        }
    }

    [RelayCommand]
    public void CopyUsername(KeyEntry? entry)
    {
        if (entry == null || string.IsNullOrEmpty(entry.UsernameOrEmail) || _dispatcherQueue == null) return;
        ClipboardHelper.CopyToClipboard(entry.UsernameOrEmail, _dispatcherQueue, 60);
        StatusMessage = $"Логин «{entry.UsernameOrEmail}» скопирован в буфер!";
    }

    [RelayCommand]
    public void CopySecret(KeyEntry? entry)
    {
        if (entry == null || string.IsNullOrEmpty(entry.SecretValue) || _dispatcherQueue == null) return;
        int timeout = _vaultService.Settings.ClearClipboardAfterSeconds ? _vaultService.Settings.ClipboardClearTimeout : 0;
        ClipboardHelper.CopyToClipboard(entry.SecretValue, _dispatcherQueue, timeout);
        StatusMessage = timeout > 0
            ? $"Пароль/ключ скопирован! Буфер очистится через {timeout} сек."
            : "Пароль/ключ скопирован в буфер.";
    }

    [RelayCommand]
    public void ShowStylizedHint(KeyEntry? entry)
    {
        if (entry == null) return;

        TeachingTipTitle = entry.Title;
        string sub = string.IsNullOrEmpty(entry.UsernameOrEmail) ? entry.TargetAppOrUrl : entry.UsernameOrEmail;
        if (!string.IsNullOrWhiteSpace(entry.TagsDisplayText))
            sub += $" • {entry.TagsDisplayText}";
        TeachingTipSubtitle = sub;
        TeachingTipSecret = entry.MaskedSecret;
        string extra = $"Энтропия: {entry.EntropyBits:F1} бит ({entry.CrackTimeText})";
        if (entry.CustomFields != null && entry.CustomFields.Count > 0)
        {
            extra += " | Поля: " + string.Join(", ", entry.CustomFields.Select(kv => $"{kv.Key}={kv.Value}"));
        }
        TeachingTipStatus = $"{entry.ExpirationStatusText} • {extra}";
        TeachingTipBadgeColor = entry.StatusBadgeColor;
        SelectedEntry = entry;
        IsTeachingTipOpen = true;
    }

    [RelayCommand]
    public void CopyTotpCode(KeyEntry? entry)
    {
        if (entry == null || !entry.HasTotp || string.IsNullOrEmpty(entry.CurrentTotpCode) || _dispatcherQueue == null) return;
        int timeout = _vaultService.Settings.ClearClipboardAfterSeconds ? _vaultService.Settings.ClipboardClearTimeout : 0;
        ClipboardHelper.CopyToClipboard(entry.CurrentTotpCode.Replace(" ", ""), _dispatcherQueue, timeout);
        StatusMessage = $"Код 2FA «{entry.CurrentTotpCode}» скопирован в буфер!";
    }

    [RelayCommand]
    public void GenerateTotpSecret()
    {
        EditTotpSecret = _totpService.GenerateSecretKey();
    }

    [RelayCommand]
    public async Task CheckPasswordPwnedAsync(KeyEntry? entry)
    {
        if (entry == null || string.IsNullOrEmpty(entry.SecretValue)) return;
        StatusMessage = $"Проверка пароля «{entry.Title}» в Have I Been Pwned...";
        try
        {
            var res = await _pwnedPasswordsService.CheckPasswordAsync(entry.SecretValue);
            entry.PwnedBreachCount = res.BreachCount;
            entry.PwnedLastCheckedAt = DateTimeOffset.UtcNow;
            await _vaultService.SaveVaultAsync();
            RefreshFilteredList();

            if (res.IsBreached)
            {
                StatusMessage = $"⚠️ Внимание! Пароль для «{entry.Title}» обнаружен в {res.BreachCount:N0} утечках!";
            }
            else
            {
                StatusMessage = $"🛡️ Пароль для «{entry.Title}» безопасен (не найден в утечках).";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Ошибка аудита пароля: {ex.Message}";
        }
    }

    [RelayCommand]
    public async Task AuditAllPasswordsAsync()
    {
        if (IsAuditingPasswords || _vaultService.CurrentContainer == null || _vaultService.CurrentContainer.Entries.Count == 0) return;
        IsAuditingPasswords = true;
        AuditProgress = 0;
        AuditStatusText = "Проверка паролей по базе Have I Been Pwned...";
        StatusMessage = "Запущен аудит безопасности паролей (k-Anonymity)...";

        try
        {
            var prog = new Progress<double>(p =>
            {
                _dispatcherQueue?.TryEnqueue(() =>
                {
                    AuditProgress = p;
                    AuditStatusText = $"Проверка базы: {p:F0}%";
                });
            });

            var summary = await _pwnedPasswordsService.AuditVaultAsync(_vaultService.CurrentContainer.Entries, prog);
            BreachedPasswordsCount = summary.BreachedEntriesCount;
            await _vaultService.SaveVaultAsync();
            RefreshFilteredList();

            if (summary.BreachedEntriesCount > 0)
            {
                AuditStatusText = $"Найдено скомпрометированных: {summary.BreachedEntriesCount}";
                StatusMessage = $"⚠️ Аудит завершён: обнаружено {summary.BreachedEntriesCount} скомпрометированных паролей!";
            }
            else
            {
                AuditStatusText = "Все пароли безопасны!";
                StatusMessage = $"🛡️ Аудит завершён: все {summary.CleanEntriesCount} паролей безопасны.";
            }
        }
        catch (Exception ex)
        {
            AuditStatusText = "Ошибка проверки утечек";
            StatusMessage = $"Ошибка при аудите утечек: {ex.Message}";
        }
        finally
        {
            IsAuditingPasswords = false;
        }
    }

    [RelayCommand]
    public void GeneratePassword()
    {
        EditSecret = PasswordGenerator.Generate(
            GenPasswordLength,
            GenUseUpper,
            GenUseLower,
            GenUseDigits,
            GenUseSpecial,
            GenExcludeAmbiguous);
    }

    [RelayCommand]
    public void GenerateLicenseKey()
    {
        EditSecret = PasswordGenerator.GenerateLicenseKey();
        EditCategory = KeyCategory.License;
    }

    [RelayCommand]
    public void SetExpirationPreset(string daysStr)
    {
        if (int.TryParse(daysStr, out int days))
        {
            EditHasExpiration = true;
            EditExpirationDate = DateTimeOffset.Now.AddDays(days);
        }
    }

    [RelayCommand]
    public async Task SyncCloudAsync()
    {
        IsLoading = true;
        StatusMessage = "Синхронизация с облачным диском...";
        try
        {
            bool ok = await _vaultService.SyncWithCloudAsync();
            if (ok)
            {
                RefreshFilteredList();
                UpdateCloudStatus();
                StatusMessage = "Синхронизация с облаком успешно завершена!";
            }
            else
            {
                StatusMessage = "Облачная папка не настроена или недоступна.";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Ошибка синхронизации: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public void OpenSettingsDialog()
    {
        SettingsLocalPath = _vaultService.LocalVaultPath;
        SettingsCloudFolder = _vaultService.Settings.CloudSyncFolderPath;
        SettingsCloudEnabled = _vaultService.Settings.CloudSyncEnabled;
        SettingsAutoLockMinutes = _vaultService.Settings.AutoLockMinutes;
        var savedTheme = _vaultService.Settings.Theme ?? "MIDNIGHT";
        SelectedTheme = AvailableThemes.FirstOrDefault(t => t.StartsWith(savedTheme, StringComparison.OrdinalIgnoreCase)) ?? AvailableThemes[0];

        var savedAccent = _vaultService.Settings.Accent ?? "Violet";
        SelectedAccent = AvailableAccents.FirstOrDefault(a => a.StartsWith(savedAccent, StringComparison.OrdinalIgnoreCase)) ?? AvailableAccents[0];

        var savedLang = string.IsNullOrEmpty(_vaultService.Settings.Language) ? LocalizationService.GetSystemLanguageCode() : _vaultService.Settings.Language;
        SelectedLanguage = AvailableLanguages.FirstOrDefault(l => l.Contains($"({savedLang})", StringComparison.OrdinalIgnoreCase)) ?? AvailableLanguages[0];

        var savedFont = _vaultService.Settings.FontFamily ?? "Inter Variable";
        SelectedFont = AvailableFonts.FirstOrDefault(f => f.StartsWith(savedFont, StringComparison.OrdinalIgnoreCase)) ?? AvailableFonts[0];

        if (!IsUpdateAvailable)
        {
            UpdateStatusText = $"Установлена актуальная версия ({UpdateService.CurrentVersionString})";
        }

        ApplyCurrentTheme();
        ApplyCurrentLanguage(LocalizationService.GetLanguage(SelectedLanguage).Code);
        IsSettingsDialogVisible = true;
    }

    [RelayCommand]
    public async Task SaveSettingsAsync()
    {
        _vaultService.Settings.CloudSyncFolderPath = SettingsCloudFolder.Trim();
        _vaultService.Settings.CloudSyncEnabled = SettingsCloudEnabled;
        _vaultService.Settings.AutoLockMinutes = SettingsAutoLockMinutes;
        _vaultService.Settings.Theme = SelectedTheme;
        _vaultService.Settings.Accent = SelectedAccent;
        _vaultService.Settings.Language = SelectedLanguage;
        _vaultService.Settings.FontFamily = SelectedFont;
        if (SettingsWindowsHelloEnabled != _vaultService.Settings.WindowsHelloEnabled)
        {
            _vaultService.Settings.WindowsHelloEnabled = SettingsWindowsHelloEnabled;
            if (!SettingsWindowsHelloEnabled)
            {
                await _windowsHelloService.DisableAsync();
                IsWindowsHelloEnrolled = false;
            }
        }
        await _vaultService.SaveSettingsAsync();
        UpdateCloudStatus();
        IsSettingsDialogVisible = false;
        StatusMessage = "Настройки успешно сохранены.";
    }

    partial void OnSelectedThemeChanged(string value)
    {
        ApplyCurrentTheme();
    }

    partial void OnSelectedAccentChanged(string value)
    {
        var accentDef = ThemeService.GetAccent(value);
        SelectedAccentHex = accentDef.Hex;
        ApplyCurrentTheme();
    }

    partial void OnSelectedLanguageChanged(string value)
    {
        var langDef = LocalizationService.GetLanguage(value);
        ApplyCurrentLanguage(langDef.Code);
    }

    partial void OnSelectedFontChanged(string value)
    {
        var fontDef = ThemeService.GetFont(value);
        CurrentFontFamily = new FontFamily(fontDef.FontFamily);
    }

    public void ApplyCurrentTheme()
    {
        try
        {
            var themeDef = ThemeService.GetTheme(SelectedTheme);
            var accentDef = ThemeService.GetAccent(SelectedAccent);
            var fontDef = ThemeService.GetFont(SelectedFont);

            CurrentBackgroundBrush = new SolidColorBrush(ThemeService.ColorFromHex(themeDef.BackgroundHex));
            CurrentHeaderBrush = new SolidColorBrush(ThemeService.ColorFromHex(themeDef.HeaderHex));
            CurrentSurfaceBrush = new SolidColorBrush(ThemeService.ColorFromHex(themeDef.SurfaceHex));
            CurrentCardBrush = new SolidColorBrush(ThemeService.ColorFromHex(themeDef.CardHex));
            CurrentCardStrokeBrush = new SolidColorBrush(ThemeService.ColorFromHex(themeDef.CardStrokeHex));
            CurrentTextPrimaryBrush = new SolidColorBrush(ThemeService.ColorFromHex(themeDef.TextPrimaryHex));
            CurrentTextSecondaryBrush = new SolidColorBrush(ThemeService.ColorFromHex(themeDef.TextSecondaryHex));
            CurrentAccentBrush = new SolidColorBrush(ThemeService.ColorFromHex(accentDef.Hex));
            CurrentFontFamily = new FontFamily(fontDef.FontFamily);

            ThemeService.ApplyToResources(themeDef.Key, accentDef.Key, fontDef.Key, MainWindow.Instance?.Content as FrameworkElement);
            SyncTrayMenuStyle();
        }
        catch { }
    }

    private void SyncTrayMenuStyle()
    {
        try
        {
            var themeDef = ThemeService.GetTheme(SelectedTheme);
            var accentDef = ThemeService.GetAccent(SelectedAccent);
            var langDef = LocalizationService.GetLanguage(SelectedLanguage);

            string hoverHex = themeDef.Key.Equals("MIDNIGHT", StringComparison.OrdinalIgnoreCase)
                ? "#2E2652"
                : themeDef.CardHex;

            string restoreLabel = langDef.Code switch
            {
                "en" => "Restore STORM KEYS",
                "de" => "STORM KEYS wiederherstellen",
                "fr" => "Restaurer STORM KEYS",
                "es" => "Restaurar STORM KEYS",
                "it" => "Ripristina STORM KEYS",
                "pt" => "Restaurar STORM KEYS",
                "zh" => "打开 STORM KEYS",
                "ja" => "STORM KEYS を開く",
                "ko" => "STORM KEYS 열기",
                "hi" => "STORM KEYS खोलें",
                _ => "Развернуть STORM KEYS"
            };

            string aboutLabel = langDef.Code switch
            {
                "en" => "About",
                "de" => "Über das Programm",
                "fr" => "À propos",
                "es" => "Acerca de",
                "it" => "Informazioni",
                "pt" => "Sobre",
                "zh" => "关于程序",
                "ja" => "バージョン情報",
                "ko" => "프로그램 정보",
                "hi" => "प्रोग्राम के बारे में",
                _ => "О программе"
            };

            string exitLabel = langDef.Code switch
            {
                "en" => "Exit",
                "de" => "Beenden",
                "fr" => "Quitter",
                "es" => "Salir",
                "it" => "Esci",
                "pt" => "Sair",
                "zh" => "退出",
                "ja" => "終了",
                "ko" => "종료",
                "hi" => "बाहर निकलें",
                _ => "Выход"
            };

            MainWindow.Instance?.TrayService?.ApplyTheme(
                themeDef.HeaderHex,
                hoverHex,
                accentDef.Hex,
                themeDef.TextPrimaryHex,
                themeDef.CardStrokeHex,
                restoreLabel,
                aboutLabel,
                exitLabel);
        }
        catch { }
    }

    public void ApplyCurrentLanguage(string langCode)
    {
        try
        {
            UiTitle = LocalizationService.GetString("AppTitle", langCode);
            UiSubtitle = LocalizationService.GetString("AppSubtitle", langCode);
            UiSearchPlaceholder = LocalizationService.GetString("SearchPlaceholder", langCode);
            UiAdd = LocalizationService.GetString("Add", langCode);
            UiImport = LocalizationService.GetString("Import", langCode);
            UiExport = LocalizationService.GetString("Export", langCode);
            UiSettings = LocalizationService.GetString("Settings", langCode);
            UiLock = LocalizationService.GetString("Lock", langCode);
            UiTotalKeys = LocalizationService.GetString("TotalKeys", langCode);
            UiExpiringSoon = LocalizationService.GetString("ExpiringSoon", langCode);
            UiExpiredLicenses = LocalizationService.GetString("ExpiredLicenses", langCode);
            UiSyncStatus = LocalizationService.GetString("SyncStatus", langCode);
            UiCategoryAll = LocalizationService.GetString("CategoryAll", langCode);
            UiExpirationAll = LocalizationService.GetString("ExpirationAll", langCode);
            UiSettingsTitle = LocalizationService.GetString("SettingsTitle", langCode);
            UiSettingsLocalVault = LocalizationService.GetString("SettingsLocalVault", langCode);
            UiSettingsCloudSync = LocalizationService.GetString("SettingsCloudSync", langCode);
            UiSettingsTheme = LocalizationService.GetString("SettingsTheme", langCode);
            UiSettingsAccent = LocalizationService.GetString("SettingsAccent", langCode);
            UiSettingsLanguage = LocalizationService.GetString("SettingsLanguage", langCode);
            UiSettingsFont = LocalizationService.GetString("SettingsFont", langCode);
            UiSettingsAutoLock = LocalizationService.GetString("SettingsAutoLock", langCode);
            UiSettingsSave = LocalizationService.GetString("SettingsSave", langCode);
            UiSettingsClose = LocalizationService.GetString("SettingsClose", langCode);
            UiCheckUpdates = LocalizationService.GetString("CheckUpdates", langCode);
            UiInstallUpdate = LocalizationService.GetString("InstallUpdate", langCode);
            UiPlatformCert = LocalizationService.GetString("PlatformCert", langCode);
            UiPlatformCertDesc = LocalizationService.GetString("PlatformCertDesc", langCode);
            UiUpdatesHeader = LocalizationService.GetString("UpdatesHeader", langCode);
            UiTooltipAdd = LocalizationService.GetString("TooltipAdd", langCode);
            UiTooltipImport = LocalizationService.GetString("TooltipImport", langCode);
            UiTooltipExport = LocalizationService.GetString("TooltipExport", langCode);
            UiTooltipSettings = LocalizationService.GetString("TooltipSettings", langCode);
            UiTooltipLock = LocalizationService.GetString("TooltipLock", langCode);
            UiTooltipCopyUsername = LocalizationService.GetString("TooltipCopyUsername", langCode);
            UiTooltipCopyPassword = LocalizationService.GetString("TooltipCopyPassword", langCode);
            UiTooltipCardDetails = LocalizationService.GetString("TooltipCardDetails", langCode);
            UiTooltipEdit = LocalizationService.GetString("TooltipEdit", langCode);
            UiTooltipDelete = LocalizationService.GetString("TooltipDelete", langCode);
            UiTooltipGenPassword = LocalizationService.GetString("TooltipGenPassword", langCode);
            UiTooltipGenSoftwareKey = LocalizationService.GetString("TooltipGenSoftwareKey", langCode);
            UiAboutTitle = LocalizationService.GetString("AboutTitle", langCode);
            UiAboutPublisher = LocalizationService.GetString("AboutPublisher", langCode);
            UiAboutVersion = LocalizationService.GetString("AboutVersion", langCode);
            UiAboutCert = LocalizationService.GetString("AboutCert", langCode);
            UiAboutDesc = LocalizationService.GetString("AboutDesc", langCode);
            UiAboutClose = LocalizationService.GetString("AboutClose", langCode);
            UiWindowsHelloUnlock = LocalizationService.GetString("WindowsHelloUnlock", langCode);
            UiWindowsHelloToggle = LocalizationService.GetString("WindowsHelloToggle", langCode);
            UiTotpLabel = LocalizationService.GetString("TotpCode", langCode);
            UiTotpCopyTooltip = LocalizationService.GetString("TotpCopyTooltip", langCode);
            UiTotpSecretLabel = LocalizationService.GetString("TotpSecretLabel", langCode);
            UiTotpGenerate = LocalizationService.GetString("TotpGenerate", langCode);
            UiAuditPasswords = LocalizationService.GetString("PwnedAuditButton", langCode);

            SyncTrayMenuStyle();
        }
        catch { }
    }

    [RelayCommand]
    public async Task ImportFromClipboardAsync()
    {
        try
        {
            var dataPackageView = Windows.ApplicationModel.DataTransfer.Clipboard.GetContent();
            if (dataPackageView.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.Text))
            {
                string text = await dataPackageView.GetTextAsync();
                var res = _importExportService.ImportFromClipboardText(text);
                if (res.ImportedCount > 0 && _vaultService.CurrentContainer != null)
                {
                    _vaultService.CurrentContainer.Entries.AddRange(res.ImportedEntries);
                    await _vaultService.SaveVaultAsync();
                    RefreshFilteredList();
                    StatusMessage = $"Импортировано {res.ImportedCount} зап. из буфера обмена ({res.FormatDetected})!";
                }
                else
                {
                    // Если в буфере произвольный текст — открываем окно добавления с подставленными данными
                    OpenAddDialog();
                    if (!string.IsNullOrWhiteSpace(text))
                        EditNotes = text.Trim();
                    StatusMessage = "Открыта форма добавления новой записи.";
                }
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Ошибка импорта из буфера: {ex.Message}";
        }
    }

    [RelayCommand]
    public void OpenImportDialog()
    {
        ImportSummaryText = "Выберите CSV, JSON, .env или .txt файл экспорта из браузера (Chrome, Edge, Firefox) или менеджера паролей.";
        IsImportDialogVisible = true;
    }

    [RelayCommand]
    public async Task ProcessFileImportAsync(string filePath)
    {
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath) || _vaultService.CurrentContainer == null)
            return;

        IsLoading = true;
        try
        {
            ImportResult res;
            if (filePath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                res = await _importExportService.ImportFromJsonAsync(filePath);
            else
                res = await _importExportService.ImportFromCsvAsync(filePath);

            if (res.ImportedCount > 0)
            {
                _vaultService.CurrentContainer.Entries.AddRange(res.ImportedEntries);
                await _vaultService.SaveVaultAsync();
                RefreshFilteredList();
                ImportSummaryText = $"Успешно! Формат: {res.FormatDetected}. Добавлено: {res.ImportedCount}, пропущено: {res.SkippedCount}.";
                StatusMessage = $"Импортировано {res.ImportedCount} записей ({res.FormatDetected}).";
            }
            else
            {
                ImportSummaryText = "Файл прочитан, но не найдено валидных записей для импорта.";
            }
        }
        catch (Exception ex)
        {
            ImportSummaryText = $"Ошибка импорта: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public async Task CheckForUpdatesAsync()
    {
        if (IsCheckingUpdate) return;
        IsCheckingUpdate = true;
        UpdateStatusText = "Проверка обновлений...";
        try
        {
            var update = await _updateService.CheckForUpdatesAsync(silent: false);
            if (update != null && UpdateService.IsNewerVersion(update.Version, UpdateService.CurrentVersion))
            {
                IsUpdateAvailable = true;
                UpdateVersion = update.Version;
                UpdateChangelog = update.Changelog;
                UpdateDownloadUrl = update.DownloadUrl;
                UpdateFileName = update.FileName;
                UpdateStatusText = $"Доступно обновление: {update.Version}";
                StatusMessage = $"Найдена новая версия {update.Version}!";
            }
            else
            {
                IsUpdateAvailable = false;
                UpdateVersion = string.Empty;
                UpdateStatusText = $"У вас установлена актуальная версия ({UpdateService.CurrentVersionString})";
                StatusMessage = $"У вас установлена самая актуальная версия STORM KEYS ({UpdateService.CurrentVersionString}).";
            }
        }
        catch
        {
            IsUpdateAvailable = false;
            UpdateStatusText = "Ошибка подключения к серверу обновлений";
            StatusMessage = "Не удалось проверить обновления. Проверьте интернет-соединение.";
        }
        finally
        {
            IsCheckingUpdate = false;
        }
    }

    [RelayCommand]
    public async Task InstallUpdateAsync()
    {
        if (string.IsNullOrEmpty(UpdateDownloadUrl)) return;
        StatusMessage = $"Загрузка обновления {UpdateVersion}...";
        var info = new UpdateInfo(UpdateVersion, UpdateChangelog, UpdateDownloadUrl, UpdateFileName);
        var prog = new Progress<double>(p =>
        {
            _dispatcherQueue?.TryEnqueue(() =>
            {
                UpdateProgress = p;
                StatusMessage = $"Загрузка обновления: {p:F0}%";
            });
        });
        bool ok = await _updateService.DownloadAndInstallAsync(info, prog);
        if (!ok)
        {
            StatusMessage = "Ошибка загрузки или запуска установщика обновления.";
        }
    }
}
