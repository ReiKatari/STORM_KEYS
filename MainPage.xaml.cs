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
    public MainViewModel ViewModel => App.ViewModel;

    public Visibility BoolToVis(bool val) => val ? Visibility.Visible : Visibility.Collapsed;
    public Visibility InvBoolToVis(bool val) => val ? Visibility.Collapsed : Visibility.Visible;
    public Visibility StringToVis(string? val) => string.IsNullOrWhiteSpace(val) ? Visibility.Collapsed : Visibility.Visible;
    public bool HasText(string? val) => !string.IsNullOrWhiteSpace(val);
    public bool Not(bool val) => !val;
    public SolidColorBrush HexToBrush(string? hex) => BrushHelper.FromHex(hex);

    public List<string> CategoryFilterOptions { get; } = new()
    {
        "Все категории",
        "Учетные записи",
        "Лицензии ПО",
        "API Ключи",
        "Сертификаты / SSH",
        "Серверы",
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

    public List<string> CategoryEditOptions { get; } = new()
    {
        "Учетная запись (Логин/Пароль)",
        "Лицензионный ключ ПО",
        "API Ключ / Секретный токен",
        "Сертификат / SSH Ключ",
        "Сервер / База данных",
        "Другое"
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
        set => ViewModel.EditCategory = (KeyCategory)value;
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
        InitializeComponent();
        Loaded += MainPage_Loaded;
        PreviewKeyDown += MainPage_PreviewKeyDown;
    }

    private void MainPage_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Escape)
        {
            if (ViewModel == null) return;

            // Стилизованная подсказка (TeachingTip)
            if (ViewModel.IsTeachingTipOpen)
            {
                ViewModel.IsTeachingTipOpen = false;
                e.Handled = true;
                return;
            }

            // Окно настроек
            if (ViewModel.IsSettingsDialogVisible)
            {
                ViewModel.IsSettingsDialogVisible = false;
                e.Handled = true;
                return;
            }

            // Окно добавления/редактирования записи
            if (ViewModel.IsEditDialogVisible)
            {
                ViewModel.IsEditDialogVisible = false;
                e.Handled = true;
                return;
            }

            // Диалог импорта
            if (ViewModel.IsImportDialogVisible)
            {
                ViewModel.IsImportDialogVisible = false;
                e.Handled = true;
                return;
            }

            // Диалог "О программе"
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

    private void UnlockPasswordBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter)
        {
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
