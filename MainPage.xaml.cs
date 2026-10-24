using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using StormKeys.Helpers;
using StormKeys.Models;
using StormKeys.ViewModels;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace StormKeys;

public sealed partial class MainPage : Page
{
    public static MainPage? Instance { get; private set; }
    public MainViewModel ViewModel => App.ViewModel;

    public Visibility BoolToVis(bool val) => val ? Visibility.Visible : Visibility.Collapsed;
    public Visibility InvBoolToVis(bool val) => val ? Visibility.Collapsed : Visibility.Visible;
    public Visibility StringToVis(string? val) => string.IsNullOrWhiteSpace(val) ? Visibility.Collapsed : Visibility.Visible;
    public bool HasText(string? val) => !string.IsNullOrWhiteSpace(val);
    public bool Not(bool val) => !val;
    public bool InvBool(bool val) => !val;
    public SolidColorBrush HexToBrush(string? hex) => BrushHelper.FromHex(hex);
    public string FormatGenLen(int len) => $"{len} симв.";
    public double IntToDouble(int val) => val;
    public void DoubleToGenLen(double val) => ViewModel.GenPasswordLength = (int)Math.Round(val);

    public List<string> CategoryFilterOptions { get; } = new()
    {
        "Все категории",
        "Учетные записи",
        "Лицензии ПО",
        "API Ключи",
        "Сертификаты / SSH",
        "Серверы",
        "Банковские карты",
        "Сети Wi-Fi",
        "Крипто / Seed",
        "Другое"
    };

    public List<string> ExpirationFilterOptions { get; } = new()
    {
        "Все сроки",
        "Истекающие скоро (<=30 дн.)",
        "Критические (<=7 дн.)",
        "Истёкшие",
        "Активные / Бессрочные"
    };

    public List<string> SortOrderOptions { get; } = new()
    {
        "По приоритету срока",
        "По частоте использования",
        "По алфавиту (А–Я)",
        "Сначала новые",
        "По надёжности",
        "По категории"
    };

    public List<string> CategoryEditOptions { get; } = new()
    {
        "Учетная запись (Сайт / Приложение)",
        "Лицензионный ключ ПО / Игры",
        "API Ключ / Bearer Токен / Webhook",
        "Сертификат / SSH / PGP Ключ",
        "Сервер / База данных / RDP / VPN",
        "Защищённая заметка / PIN-код",
        "Банковская карта / Платёжные данные",
        "Сеть Wi-Fi / Роутер",
        "Криптокошелёк / Seed-фраза"
    };

    public List<string> AutoLockOptions { get; } = new()
    {
        "Отключено",
        "Через 5 минут",
        "Через 15 минут",
        "Через 30 минут",
        "Через 1 час"
    };

    public int EditCategoryIndex
    {
        get => (int)ViewModel.EditCategory;
        set
        {
            if (value >= 0 && value <= 8)
                ViewModel.EditCategory = (KeyCategory)value;
        }
    }

    public int AutoLockIndex
    {
        get => ViewModel.SettingsAutoLockMinutes switch
        {
            5 => 1,
            15 => 2,
            30 => 3,
            60 => 4,
            _ => 0
        };
        set => ViewModel.SettingsAutoLockMinutes = value switch
        {
            1 => 5,
            2 => 15,
            3 => 30,
            4 => 60,
            _ => 0
        };
    }

    public DateTimeOffset EditDatePickerDate
    {
        get => ViewModel.EditExpirationDate ?? DateTimeOffset.Now.AddYears(1);
        set => ViewModel.EditExpirationDate = value;
    }

    public MainPage()
    {
        Instance = this;
        InitializeComponent();
        Loaded += MainPage_Loaded;
        PreviewKeyDown += MainPage_PreviewKeyDown;
    }

    public void FocusSearchBoxForSpotlight()
    {
        if (!ViewModel.IsVaultLocked && SearchBox != null)
        {
            SearchBox.Focus(FocusState.Programmatic);
        }
    }

    private void SearchBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        if (!ViewModel.IsVaultLocked && ViewModel.FilteredEntries.Count > 0)
        {
            _ = ViewModel.AutoTypeEntryCommand.ExecuteAsync(ViewModel.FilteredEntries[0]);
        }
    }

    private void MainPage_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        var ctrl = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control);
        bool isCtrlDown = (ctrl & Windows.UI.Core.CoreVirtualKeyStates.Down) == Windows.UI.Core.CoreVirtualKeyStates.Down;

        if (isCtrlDown && e.Key == Windows.System.VirtualKey.F)
        {
            SearchBox?.Focus(FocusState.Programmatic);
            e.Handled = true;
            return;
        }

        if (isCtrlDown && e.Key == Windows.System.VirtualKey.N)
        {
            if (!ViewModel.IsVaultLocked)
            {
                ViewModel.OpenAddDialogCommand.Execute(null);
                e.Handled = true;
                return;
            }
        }

        if (isCtrlDown && e.Key == Windows.System.VirtualKey.V)
        {
            if (!ViewModel.IsVaultLocked &&
                !ViewModel.IsEditDialogVisible &&
                !ViewModel.IsSettingsDialogVisible &&
                FocusManager.GetFocusedElement(this.XamlRoot) is not TextBox and not PasswordBox)
            {
                _ = ViewModel.ImportFromClipboardCommand.ExecuteAsync(null);
                e.Handled = true;
                return;
            }
        }

        if (isCtrlDown && e.Key == Windows.System.VirtualKey.L)
        {
            if (!ViewModel.IsVaultLocked)
            {
                _ = ViewModel.LockVaultCommand.ExecuteAsync(null);
                e.Handled = true;
                return;
            }
        }

        if (isCtrlDown && e.Key == Windows.System.VirtualKey.B)
        {
            if (!ViewModel.IsVaultLocked && (ViewModel.SelectedEntry ?? ViewModel.FilteredEntries.FirstOrDefault()) is { } targetUser)
            {
                ViewModel.CopyUsernameCommand.Execute(targetUser);
                e.Handled = true;
                return;
            }
        }

        var alt = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Menu);
        bool isAltDown = (alt & Windows.UI.Core.CoreVirtualKeyStates.Down) == Windows.UI.Core.CoreVirtualKeyStates.Down;
        if (isAltDown && !isCtrlDown && !ViewModel.IsVaultLocked)
        {
            int idx = (int)e.Key - (int)Windows.System.VirtualKey.Number1;
            if (idx >= 0 && idx < 9 && idx < ViewModel.FilteredEntries.Count)
            {
                ViewModel.CopySecretCommand.Execute(ViewModel.FilteredEntries[idx]);
                e.Handled = true;
                return;
            }
        }

        if (isCtrlDown && e.Key == Windows.System.VirtualKey.D)
        {
            if (!ViewModel.IsVaultLocked && ViewModel.SelectedEntry != null)
            {
                _ = ViewModel.DuplicateEntryCommand.ExecuteAsync(ViewModel.SelectedEntry);
                e.Handled = true;
                return;
            }
        }

        if (isCtrlDown && e.Key == Windows.System.VirtualKey.Z)
        {
            if (!ViewModel.IsVaultLocked && ViewModel.CanUndoDelete)
            {
                _ = ViewModel.UndoDeleteCommand.ExecuteAsync(null);
                e.Handled = true;
                return;
            }
        }

        if (e.Key == Windows.System.VirtualKey.F5)
        {
            if (!ViewModel.IsVaultLocked)
            {
                ViewModel.RefreshFilteredList();
                e.Handled = true;
                return;
            }
        }

        if (e.Key == Windows.System.VirtualKey.Escape)
        {
            if (ViewModel == null) return;

            if (ViewModel.IsQrMatrixDialogVisible)
            {
                ViewModel.IsQrMatrixDialogVisible = false;
                e.Handled = true;
                return;
            }

            if (ViewModel.IsSecurityAuditDialogVisible)
            {
                ViewModel.IsSecurityAuditDialogVisible = false;
                e.Handled = true;
                return;
            }

            if (ViewModel.IsTeachingTipOpen)
            {
                ViewModel.IsTeachingTipOpen = false;
                e.Handled = true;
                return;
            }

            if (ViewModel.IsSettingsDialogVisible)
            {
                ViewModel.IsSettingsDialogVisible = false;
                e.Handled = true;
                return;
            }

            if (ViewModel.IsEditDialogVisible)
            {
                ViewModel.IsEditDialogVisible = false;
                e.Handled = true;
                return;
            }

            if (ViewModel.IsImportDialogVisible)
            {
                ViewModel.IsImportDialogVisible = false;
                e.Handled = true;
                return;
            }

            if (ViewModel.IsAboutDialogVisible)
            {
                ViewModel.IsAboutDialogVisible = false;
                e.Handled = true;
                return;
            }
        }
    }

    private void MainPage_Loaded(object sender, RoutedEventArgs e)
    {
        ViewModel.Initialize(DispatcherQueue);
    }

    private void AddEntry_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.OpenAddDialog();
    }

    private void QuickAddTemplate_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string templateKey)
        {
            ViewModel.OpenAddDialogWithTemplate(templateKey);
        }
    }

    private void ApplyTemplateInDialog_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string templateKey)
        {
            ViewModel.ApplyEntryTemplate(templateKey);
        }
    }

    private void SelectIcon_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string glyph)
        {
            ViewModel.SelectCustomIcon(glyph);
        }
    }

    private void GenLenPreset_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string lenStr)
        {
            ViewModel.SetGeneratorLengthPreset(lenStr);
        }
    }

    private void ApiTokenPreset_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string format)
        {
            ViewModel.GenerateApiTokenPreset(format);
        }
    }

    private void MacroPreset_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string seq)
        {
            ViewModel.SetAutoTypeMacroPreset(seq);
        }
    }

    private void GenSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (ViewModel != null)
        {
            int newLen = (int)Math.Round(e.NewValue);
            if (newLen >= 6 && newLen <= 64 && ViewModel.GenPasswordLength != newLen)
            {
                ViewModel.GenPasswordLength = newLen;
            }
        }
    }

    private void EditEntry_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is KeyEntry entry)
        {
            ViewModel.OpenEditDialog(entry);
        }
    }

    private void DeleteEntry_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is KeyEntry entry)
        {
            _ = ViewModel.DeleteEntryCommand.ExecuteAsync(entry);
        }
    }

    private void RestoreFromTrash_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is KeyEntry entry)
        {
            _ = ViewModel.RestoreFromTrashCommand.ExecuteAsync(entry);
        }
    }

    private void CopyUsername_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is KeyEntry entry)
        {
            ViewModel.CopyUsername(entry);
        }
    }

    private void CopySecret_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is KeyEntry entry)
        {
            ViewModel.CopySecret(entry);
        }
    }

    private void CopyTotp_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is KeyEntry entry)
        {
            _ = ViewModel.CopyTotpCodeCommand.ExecuteAsync(entry);
        }
    }

    private void CopyCardCvv_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is KeyEntry entry)
        {
            ViewModel.CopyCardCvv(entry);
        }
    }

    private void CopyCardExpiry_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is KeyEntry entry)
        {
            ViewModel.CopyCardExpiry(entry);
        }
    }

    private void CopyCardPin_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is KeyEntry entry)
        {
            ViewModel.CopyCardPin(entry);
        }
    }

    private void ConnectWifi_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is KeyEntry entry)
        {
            ViewModel.ConnectWifiCard(entry);
        }
    }

    private void ShowQrMatrix_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is KeyEntry entry)
        {
            ViewModel.ShowEntryQrMatrix(entry);
        }
    }

    private void ShowTotpQrFromCard_Click(object sender, RoutedEventArgs e) => ShowQrMatrix_Click(sender, e);

    private void ShowWifiQr_Click(object sender, RoutedEventArgs e) => ShowQrMatrix_Click(sender, e);

    private void GenApiTokenPreset_Click(object sender, RoutedEventArgs e) => ApiTokenPreset_Click(sender, e);

    private void AutoTypeMacroPreset_Click(object sender, RoutedEventArgs e) => MacroPreset_Click(sender, e);

    private void AddAttachment_Click(object sender, RoutedEventArgs e) => AttachFile_Click(sender, e);

    private void ExportAttachment_Click(object sender, RoutedEventArgs e) => SaveAttachments_Click(sender, e);

    private void ExportCurrentAttachment_Click(object sender, RoutedEventArgs e) => SaveAttachments_Click(sender, e);

    private void ExportEncryptedBackup_Click(object sender, RoutedEventArgs e) => ExportStormBackup_Click(sender, e);

    private void ImportEncryptedBackup_Click(object sender, RoutedEventArgs e) => ImportFile_Click(sender, e);

    private void CopyRecoveryCode_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is KeyEntry entry)
        {
            _ = ViewModel.CopyNextRecoveryCodeCommand.ExecuteAsync(entry);
        }
    }

    // Идея #4: Drag & Drop чипов Логина, Пароля и 2FA в браузер и программы
    private void CardUsername_DragStarting(UIElement sender, DragStartingEventArgs args)
    {
        if (sender is FrameworkElement fe && fe.DataContext is KeyEntry entry && !string.IsNullOrEmpty(entry.UsernameOrEmail))
        {
            args.Data.SetText(entry.UsernameOrEmail);
            args.Data.RequestedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Copy;
        }
    }

    private void CardSecret_DragStarting(UIElement sender, DragStartingEventArgs args)
    {
        if (sender is FrameworkElement fe && fe.DataContext is KeyEntry entry && !string.IsNullOrEmpty(entry.SecretValue))
        {
            args.Data.SetText(entry.SecretValue);
            args.Data.RequestedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Copy;
        }
    }

    private void CardTotp_DragStarting(UIElement sender, DragStartingEventArgs args)
    {
        if (sender is FrameworkElement fe && fe.DataContext is KeyEntry entry && !string.IsNullOrEmpty(entry.CurrentTotpCode))
        {
            args.Data.SetText(entry.CurrentTotpCode.Replace(" ", ""));
            args.Data.RequestedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Copy;
        }
    }

    private void QuickAutoType_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is KeyEntry entry)
        {
            _ = ViewModel.AutoTypeEntryCommand.ExecuteAsync(entry);
        }
    }

    private void AutoTypeTotp_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is KeyEntry entry)
        {
            _ = ViewModel.AutoTypeTotpCommand.ExecuteAsync(entry);
        }
    }

    private void PeekSecret_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is KeyEntry entry)
        {
            ViewModel.PeekSecret(entry);
        }
    }

    private void DuplicateEntry_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is KeyEntry entry)
        {
            _ = ViewModel.DuplicateEntryCommand.ExecuteAsync(entry);
        }
    }

    private void CopySummary_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is KeyEntry entry)
        {
            ViewModel.CopyFullSummary(entry);
        }
    }

    private async void AttachFile_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new FileOpenPicker();
            if (MainWindow.Instance != null)
            {
                var hwnd = WindowNative.GetWindowHandle(MainWindow.Instance);
                InitializeWithWindow.Initialize(picker, hwnd);
            }

            picker.ViewMode = PickerViewMode.List;
            picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
            picker.FileTypeFilter.Add("*");

            var file = await picker.PickSingleFileAsync();
            if (file != null)
            {
                await ViewModel.AttachFileToEditDialogAsync(file.Path);
            }
        }
        catch (Exception ex)
        {
            ViewModel.StatusMessage = $"Ошибка выбора файла вложения: {ex.Message}";
        }
    }

    private async void SaveAttachments_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            KeyEntry? entry = (sender as FrameworkElement)?.Tag as KeyEntry;
            var picker = new FolderPicker();
            if (MainWindow.Instance != null)
            {
                var hwnd = WindowNative.GetWindowHandle(MainWindow.Instance);
                InitializeWithWindow.Initialize(picker, hwnd);
            }
            picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
            picker.FileTypeFilter.Add("*");

            var folder = await picker.PickSingleFolderAsync();
            if (folder != null)
            {
                await ViewModel.ExportAttachmentsToFolderAsync(entry, folder.Path);
            }
        }
        catch (Exception ex)
        {
            ViewModel.StatusMessage = $"Ошибка извлечения вложений: {ex.Message}";
        }
    }

    private async void PickTargetApp_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new FileOpenPicker();
            if (MainWindow.Instance != null)
            {
                var hwnd = WindowNative.GetWindowHandle(MainWindow.Instance);
                InitializeWithWindow.Initialize(picker, hwnd);
            }

            picker.ViewMode = PickerViewMode.List;
            picker.SuggestedStartLocation = PickerLocationId.ComputerFolder;
            picker.FileTypeFilter.Add(".exe");
            picker.FileTypeFilter.Add(".lnk");
            picker.FileTypeFilter.Add(".rdp");
            picker.FileTypeFilter.Add(".bat");
            picker.FileTypeFilter.Add(".cmd");

            var file = await picker.PickSingleFileAsync();
            if (file != null)
            {
                ViewModel.EditTargetApp = file.Path;
                if (string.IsNullOrWhiteSpace(ViewModel.EditTitle))
                {
                    ViewModel.EditTitle = Path.GetFileNameWithoutExtension(file.Name);
                }
                ViewModel.StatusMessage = $"Привязано приложение: {file.Name}";
            }
        }
        catch (Exception ex)
        {
            ViewModel.StatusMessage = $"Ошибка выбора программы: {ex.Message}";
        }
    }

    private async void ExportRecoveryKit_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new FileSavePicker();
            if (MainWindow.Instance != null)
            {
                var hwnd = WindowNative.GetWindowHandle(MainWindow.Instance);
                InitializeWithWindow.Initialize(picker, hwnd);
            }

            picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
            picker.FileTypeChoices.Add("Текстовый документ", new List<string> { ".txt" });
            picker.SuggestedFileName = $"STORM_KEYS_Recovery_Kit_{DateTime.Now:yyyyMMdd}";

            var file = await picker.PickSaveFileAsync();
            if (file != null)
            {
                await App.ImportExportService.ExportEmergencyRecoveryKitAsync(ViewModel.FilteredEntries, file.Path);
                ViewModel.StatusMessage = $"Аварийный лист восстановления сохранён: {file.Name}";
            }
        }
        catch (Exception ex)
        {
            ViewModel.StatusMessage = $"Ошибка сохранения аварийного листа: {ex.Message}";
        }
    }

    private async void ExportDotEnv_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new FileSavePicker();
            if (MainWindow.Instance != null)
            {
                var hwnd = WindowNative.GetWindowHandle(MainWindow.Instance);
                InitializeWithWindow.Initialize(picker, hwnd);
            }

            picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
            picker.FileTypeChoices.Add("Файл окружения .env", new List<string> { ".env" });
            picker.SuggestedFileName = $"STORM_KEYS_{DateTime.Now:yyyyMMdd}";

            var file = await picker.PickSaveFileAsync();
            if (file != null)
            {
                await App.ImportExportService.ExportToDotEnvAsync(ViewModel.FilteredEntries, file.Path);
                ViewModel.StatusMessage = $"Экспорт ключей и секретов в .env выполнен: {file.Name}";
            }
        }
        catch (Exception ex)
        {
            ViewModel.StatusMessage = $"Ошибка экспорта в .env: {ex.Message}";
        }
    }

    private async void ExportStormBackup_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new FileSavePicker();
            if (MainWindow.Instance != null)
            {
                var hwnd = WindowNative.GetWindowHandle(MainWindow.Instance);
                InitializeWithWindow.Initialize(picker, hwnd);
            }

            picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
            picker.FileTypeChoices.Add("Зашифрованный архив STORM", new List<string> { ".stormbackup" });
            picker.SuggestedFileName = $"STORM_KEYS_Backup_{DateTime.Now:yyyyMMdd}";

            var file = await picker.PickSaveFileAsync();
            if (file != null)
            {
                await App.ImportExportService.ExportEncryptedBackupAsync(ViewModel.FilteredEntries, file.Path, "STORM");
                ViewModel.StatusMessage = $"Зашифрованный портативный архив (.stormbackup) сохранён: {file.Name}";
            }
        }
        catch (Exception ex)
        {
            ViewModel.StatusMessage = $"Ошибка создания .stormbackup: {ex.Message}";
        }
    }

    private async void GenerateKeyfile_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new FileSavePicker();
            if (MainWindow.Instance != null)
            {
                var hwnd = WindowNative.GetWindowHandle(MainWindow.Instance);
                InitializeWithWindow.Initialize(picker, hwnd);
            }

            picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
            picker.FileTypeChoices.Add("Ключ-файл STORM (.stormkey)", new List<string> { ".stormkey" });
            picker.SuggestedFileName = "vault_master";

            var file = await picker.PickSaveFileAsync();
            if (file != null)
            {
                await ViewModel.ApplyKeyfileAsync(file.Path, createNew: true);
            }
        }
        catch (Exception ex)
        {
            ViewModel.StatusMessage = $"Ошибка создания .stormkey: {ex.Message}";
        }
    }

    private async void PickKeyfile_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new FileOpenPicker();
            if (MainWindow.Instance != null)
            {
                var hwnd = WindowNative.GetWindowHandle(MainWindow.Instance);
                InitializeWithWindow.Initialize(picker, hwnd);
            }

            picker.ViewMode = PickerViewMode.List;
            picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
            picker.FileTypeFilter.Add(".stormkey");
            picker.FileTypeFilter.Add(".key");

            var file = await picker.PickSingleFileAsync();
            if (file != null)
            {
                await ViewModel.ApplyKeyfileAsync(file.Path, createNew: false);
            }
        }
        catch (Exception ex)
        {
            ViewModel.StatusMessage = $"Ошибка выбора .stormkey: {ex.Message}";
        }
    }

    private void RestoreBackupSlot_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string slotStr)
        {
            _ = ViewModel.RestoreBackupSlotCommand.ExecuteAsync(slotStr);
        }
    }

    private void ToggleFavorite_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is KeyEntry entry)
        {
            _ = ViewModel.ToggleFavoriteCommand.ExecuteAsync(entry);
        }
    }

    private void LaunchWebsite_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is KeyEntry entry)
        {
            _ = ViewModel.LaunchUrlCommand.ExecuteAsync(entry);
        }
    }

    private void ToggleDensity_Click(object sender, RoutedEventArgs e)
    {
        _ = ViewModel.ToggleDensityCommand.ExecuteAsync(null);
    }

    private void FilterChip_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string chip)
        {
            ViewModel.FilterByChipCommand.Execute(chip);
        }
    }

    private void CheckPwned_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is KeyEntry entry)
        {
            _ = ViewModel.CheckPasswordPwnedCommand.ExecuteAsync(entry);
        }
    }

    private void ShowCardDetails_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is KeyEntry entry)
        {
            ViewModel.ShowStylizedHint(entry);
        }
    }

    private void PresetDays_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string tag)
        {
            ViewModel.SetExpirationPreset(tag);
        }
    }

    private void CloseEditDialog_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.IsEditDialogVisible = false;
    }

    private void OpenSettings_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.OpenSettingsDialog();
    }

    private void CloseSettingsDialog_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.IsSettingsDialogVisible = false;
    }

    private void CloseAboutDialog_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.IsAboutDialogVisible = false;
    }

    private void CloseQrMatrixDialog_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.IsQrMatrixDialogVisible = false;
    }

    private void CloseSecurityAuditDialog_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.IsSecurityAuditDialogVisible = false;
    }

    private void AuditFilterChip_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string chip)
        {
            ViewModel.IsSecurityAuditDialogVisible = false;
            ViewModel.FilterByChipCommand.Execute(chip);
        }
    }

    private void UnlockPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (sender is PasswordBox pb && ViewModel != null)
        {
            ViewModel.MasterPasswordInput = pb.Password;
        }
    }

    private void NewMasterPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (sender is PasswordBox pb && ViewModel != null)
        {
            ViewModel.MasterPasswordInput = pb.Password;
        }
    }

    private void ConfirmMasterPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (sender is PasswordBox pb && ViewModel != null)
        {
            ViewModel.ConfirmPasswordInput = pb.Password;
        }
    }

    private void UnlockVaultButton_Click(object sender, RoutedEventArgs e)
    {
        if (UnlockPasswordBox != null)
        {
            ViewModel.MasterPasswordInput = UnlockPasswordBox.Password;
        }
        _ = ViewModel.UnlockOrCreateVaultCommand.ExecuteAsync(null);
    }

    private void CreateVaultButton_Click(object sender, RoutedEventArgs e)
    {
        if (NewMasterPasswordBox != null)
        {
            ViewModel.MasterPasswordInput = NewMasterPasswordBox.Password;
        }
        if (ConfirmMasterPasswordBox != null)
        {
            ViewModel.ConfirmPasswordInput = ConfirmMasterPasswordBox.Password;
        }
        _ = ViewModel.UnlockOrCreateVaultCommand.ExecuteAsync(null);
    }

    private void UnlockPasswordBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter)
        {
            e.Handled = true;
            if (sender is PasswordBox pb)
            {
                ViewModel.MasterPasswordInput = pb.Password;
            }
            _ = ViewModel.UnlockOrCreateVaultCommand.ExecuteAsync(null);
        }
    }

    private void ImportMenu_Click(object sender, RoutedEventArgs e)
    {
    }

    private void ExportMenu_Click(object sender, RoutedEventArgs e)
    {
    }

    private async void ImportFile_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new FileOpenPicker();
            if (MainWindow.Instance != null)
            {
                var hwnd = WindowNative.GetWindowHandle(MainWindow.Instance);
                InitializeWithWindow.Initialize(picker, hwnd);
            }

            picker.ViewMode = PickerViewMode.List;
            picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
            picker.FileTypeFilter.Add(".csv");
            picker.FileTypeFilter.Add(".json");
            picker.FileTypeFilter.Add(".xml");
            picker.FileTypeFilter.Add(".stormbackup");
            picker.FileTypeFilter.Add(".env");
            picker.FileTypeFilter.Add(".txt");

            var file = await picker.PickSingleFileAsync();
            if (file != null)
            {
                await ViewModel.ProcessFileImportAsync(file.Path);
            }
        }
        catch (Exception ex)
        {
            ViewModel.StatusMessage = $"Ошибка открытия файла: {ex.Message}";
        }
    }

    private async void ImportClipboard_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.ImportFromClipboardCommand.ExecuteAsync(null);
    }

    private async void ExportCsv_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new FileSavePicker();
            if (MainWindow.Instance != null)
            {
                var hwnd = WindowNative.GetWindowHandle(MainWindow.Instance);
                InitializeWithWindow.Initialize(picker, hwnd);
            }

            picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
            picker.FileTypeChoices.Add("CSV Файл", new List<string> { ".csv" });
            picker.SuggestedFileName = $"STORM_KEYS_Export_{DateTime.Now:yyyyMMdd}";

            var file = await picker.PickSaveFileAsync();
            if (file != null)
            {
                await App.ImportExportService.ExportToCsvAsync(ViewModel.FilteredEntries, file.Path);
                ViewModel.StatusMessage = $"Хранилище успешно экспортировано в {file.Name}";
            }
        }
        catch (Exception ex)
        {
            ViewModel.StatusMessage = $"Ошибка экспорта: {ex.Message}";
        }
    }

    private async void ExportHtml_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new FileSavePicker();
            if (MainWindow.Instance != null)
            {
                var hwnd = WindowNative.GetWindowHandle(MainWindow.Instance);
                InitializeWithWindow.Initialize(picker, hwnd);
            }
            picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
            picker.FileTypeChoices.Add("HTML Report", new List<string> { ".html" });
            picker.SuggestedFileName = $"STORM_KEYS_Report_{DateTime.Now:yyyyMMdd}";

            var file = await picker.PickSaveFileAsync();
            if (file != null)
            {
                await ViewModel.ExportHtmlReportAsync(file.Path);
            }
        }
        catch (Exception ex)
        {
            ViewModel.StatusMessage = $"Ошибка экспорта HTML: {ex.Message}";
        }
    }

    private async void ExportJson_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new FileSavePicker();
            if (MainWindow.Instance != null)
            {
                var hwnd = WindowNative.GetWindowHandle(MainWindow.Instance);
                InitializeWithWindow.Initialize(picker, hwnd);
            }

            picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
            picker.FileTypeChoices.Add("JSON Файл", new List<string> { ".json" });
            picker.SuggestedFileName = $"STORM_KEYS_Export_{DateTime.Now:yyyyMMdd}";

            var file = await picker.PickSaveFileAsync();
            if (file != null)
            {
                await App.ImportExportService.ExportToJsonAsync(ViewModel.FilteredEntries, file.Path);
                ViewModel.StatusMessage = $"Хранилище успешно экспортировано в {file.Name}";
            }
        }
        catch (Exception ex)
        {
            ViewModel.StatusMessage = $"Ошибка экспорта: {ex.Message}";
        }
    }

    private async void PickCloudFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new FolderPicker();
            if (MainWindow.Instance != null)
            {
                var hwnd = WindowNative.GetWindowHandle(MainWindow.Instance);
                InitializeWithWindow.Initialize(picker, hwnd);
            }

            picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
            picker.FileTypeFilter.Add("*");

            var folder = await picker.PickSingleFolderAsync();
            if (folder != null)
            {
                ViewModel.SettingsCloudFolder = folder.Path;
            }
        }
        catch (Exception ex)
        {
            ViewModel.StatusMessage = $"Ошибка выбора папки: {ex.Message}";
        }
    }
}
