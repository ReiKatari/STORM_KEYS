using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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
    private DispatcherQueue? _dispatcherQueue;

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
    private string _updateStatusText = "Проверить обновления";

    // Состояния хранилища
    [ObservableProperty]
    private bool _isVaultUninitialized;

    [ObservableProperty]
    private bool _isVaultLocked = true;

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
    private string _cloudSyncStatusText = "Локальное хранилище";

    [ObservableProperty]
    private bool _isCloudSyncConfigured;

    [ObservableProperty]
    private string _certificateStatus = "STORM CERTIFIED";

    // Редактирование / Создание записи
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
    [ObservableProperty] private string _uiInstallUpdate = "Установить";
    [ObservableProperty] private string _uiPlatformCert = "STORM INTEGRATED PLATFORM";
    [ObservableProperty] private string _uiPlatformCertDesc = "Шифрование AES-256-GCM • Хеширование Argon2id • Экосистема STORM";
    [ObservableProperty] private string _uiUpdatesHeader = "Обновления STORM KEYS";

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
        IUpdateService updateService)
    {
        _vaultService = vaultService;
        _cryptoService = cryptoService;
        _importExportService = importExportService;
        _notificationService = notificationService;
        _certService = certService;
        _updateService = updateService;
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
        var savedTheme = _vaultService.Settings.Theme ?? "MIDNIGHT";
        SelectedTheme = AvailableThemes.FirstOrDefault(t => t.StartsWith(savedTheme, StringComparison.OrdinalIgnoreCase)) ?? AvailableThemes[0];

        var savedAccent = _vaultService.Settings.Accent ?? "Violet";
        SelectedAccent = AvailableAccents.FirstOrDefault(a => a.StartsWith(savedAccent, StringComparison.OrdinalIgnoreCase)) ?? AvailableAccents[0];

        var savedLang = string.IsNullOrEmpty(_vaultService.Settings.Language) ? LocalizationService.GetSystemLanguageCode() : _vaultService.Settings.Language;
        SelectedLanguage = AvailableLanguages.FirstOrDefault(l => l.Contains($"({savedLang})", StringComparison.OrdinalIgnoreCase)) ?? AvailableLanguages[0];

        var savedFont = _vaultService.Settings.FontFamily ?? "Inter Variable";
        SelectedFont = AvailableFonts.FirstOrDefault(f => f.StartsWith(savedFont, StringComparison.OrdinalIgnoreCase)) ?? AvailableFonts[0];

        ApplyCurrentTheme();
        ApplyCurrentLanguage(LocalizationService.GetLanguage(SelectedLanguage).Code);

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
                if (update != null)
                {
                    _dispatcherQueue.TryEnqueue(() =>
                    {
                        IsUpdateAvailable = true;
                        UpdateVersion = update.Version;
                        UpdateChangelog = update.Changelog;
                        UpdateDownloadUrl = update.DownloadUrl;
                        UpdateFileName = update.FileName;
                        UpdateStatusText = $"Доступно обновление: {update.Version}";
                        StatusMessage = $"Доступна новая версия STORM KEYS: {update.Version}";
                    });
                }
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

    partial void OnEditSecretChanged(string value)
    {
        var eval = PasswordGenerator.EvaluateStrength(value);
        SecretStrengthScore = eval.Score;
        SecretStrengthLabel = eval.Label;
        SecretStrengthColor = eval.ColorHex;
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

                await _vaultService.CreateVaultAsync(MasterPasswordInput);
                IsVaultUninitialized = false;
                IsVaultLocked = false;
                MasterPasswordInput = string.Empty;
                ConfirmPasswordInput = string.Empty;
                RefreshFilteredList();

                if (_dispatcherQueue != null)
                {
                    _notificationService.StartPeriodicCheck(() => _vaultService.CurrentContainer?.Entries ?? Enumerable.Empty<KeyEntry>(), _dispatcherQueue);
                }

                StatusMessage = "Новое зашифрованное хранилище успешно создано!";
            }
            else
            {
                bool success = await _vaultService.UnlockVaultAsync(MasterPasswordInput);
                if (success)
                {
                    IsVaultLocked = false;
                    MasterPasswordInput = string.Empty;
                    ConfirmPasswordInput = string.Empty;
                    RefreshFilteredList();

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
        ExpiringSoonCount = allEntries.Count(e => e.IsExpiringSoon);
        ExpiredCount = allEntries.Count(e => e.IsExpired);
        ActiveCount = allEntries.Count(e => !e.IsExpired);

        IEnumerable<KeyEntry> query = allEntries;

        // Фильтр поиска
        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            string s = SearchText.Trim().ToLowerInvariant();
            query = query.Where(e =>
                e.Title.ToLowerInvariant().Contains(s) ||
                e.UsernameOrEmail.ToLowerInvariant().Contains(s) ||
                e.TargetAppOrUrl.ToLowerInvariant().Contains(s) ||
                e.Notes.ToLowerInvariant().Contains(s));
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

        // Сортировка: сначала критические и истекающие, затем по имени
        var list = query
            .OrderBy(e => e.IsExpired ? 0 : (e.IsCriticalExpiring ? 1 : (e.IsExpiringSoon ? 2 : 3)))
            .ThenBy(e => e.DaysRemaining ?? int.MaxValue)
            .ThenBy(e => e.Title)
            .ToList();

        FilteredEntries.Clear();
        foreach (var item in list)
            FilteredEntries.Add(item);
    }

    [RelayCommand]
    public void OpenAddDialog()
    {
        IsNewEntry = true;
        EditDialogHeader = "Новая запись ключа / пароля";
        EditTitle = string.Empty;
        EditCategory = KeyCategory.Account;
        EditUsername = string.Empty;
        EditSecret = string.Empty;
        EditTargetApp = string.Empty;
        EditExpirationDate = null;
        EditHasExpiration = false;
        EditNotes = string.Empty;
        IsEditDialogVisible = true;
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
        EditUsername = entry.UsernameOrEmail;
        EditSecret = entry.SecretValue;
        EditTargetApp = entry.TargetAppOrUrl;
        EditExpirationDate = entry.ExpirationDate;
        EditHasExpiration = entry.ExpirationDate.HasValue;
        EditNotes = entry.Notes;
        IsEditDialogVisible = true;
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

        if (IsNewEntry)
        {
            var newEntry = new KeyEntry
            {
                Title = EditTitle.Trim(),
                Category = EditCategory,
                UsernameOrEmail = EditUsername.Trim(),
                SecretValue = EditSecret,
                TargetAppOrUrl = EditTargetApp.Trim(),
                ExpirationDate = EditHasExpiration ? EditExpirationDate : null,
                Notes = EditNotes.Trim(),
                CreatedAt = DateTimeOffset.UtcNow,
                ModifiedAt = DateTimeOffset.UtcNow
            };
            _vaultService.CurrentContainer.Entries.Add(newEntry);
            StatusMessage = $"Запись «{newEntry.Title}» сохранена.";
        }
        else if (SelectedEntry != null)
        {
            SelectedEntry.Title = EditTitle.Trim();
            SelectedEntry.Category = EditCategory;
            SelectedEntry.UsernameOrEmail = EditUsername.Trim();
            SelectedEntry.SecretValue = EditSecret;
            SelectedEntry.TargetAppOrUrl = EditTargetApp.Trim();
            SelectedEntry.ExpirationDate = EditHasExpiration ? EditExpirationDate : null;
            SelectedEntry.Notes = EditNotes.Trim();
            SelectedEntry.ModifiedAt = DateTimeOffset.UtcNow;
            // Сброс флагов уведомлений, если дата изменилась
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

    [RelayCommand]
    public async Task DeleteEntryAsync(KeyEntry? entry)
    {
        if (entry == null || _vaultService.CurrentContainer == null) return;

        _vaultService.CurrentContainer.Entries.Remove(entry);
        await _vaultService.SaveVaultAsync();
        RefreshFilteredList();
        StatusMessage = $"Запись «{entry.Title}» удалена.";
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
        TeachingTipSubtitle = string.IsNullOrEmpty(entry.UsernameOrEmail) ? entry.TargetAppOrUrl : entry.UsernameOrEmail;
        TeachingTipSecret = entry.MaskedSecret;
        TeachingTipStatus = entry.ExpirationStatusText;
        TeachingTipBadgeColor = entry.StatusBadgeColor;
        SelectedEntry = entry;
        IsTeachingTipOpen = true;
    }

    [RelayCommand]
    public void GeneratePassword()
    {
        EditSecret = PasswordGenerator.Generate(20);
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
                    StatusMessage = $"Импортировано {res.ImportedCount} зап. из буфера обмена!";
                }
                else
                {
                    StatusMessage = "В буфере обмена не обнаружено распознаваемых учетных данных или ключей.";
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
        ImportSummaryText = "Выберите CSV или JSON файл экспорта из браузера (Chrome, Edge, Firefox) или менеджера паролей.";
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
                StatusMessage = $"Импортировано {res.ImportedCount} записей.";
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
            if (update != null)
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
                UpdateStatusText = "У вас установлена актуальная версия";
                StatusMessage = "У вас установлена самая актуальная версия STORM KEYS.";
            }
        }
        catch
        {
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
