using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using StormKeys.Models;

namespace StormKeys.Services;

public interface IVaultService
{
    bool IsVaultCreated { get; }
    bool IsUnlocked { get; }
    VaultContainer? CurrentContainer { get; }
    AppSettings Settings { get; }
    string LocalVaultPath { get; }

    Task InitializeAsync();
    Task CreateVaultAsync(string masterPassword);
    Task<bool> UnlockVaultAsync(string masterPassword);
    Task LockVaultAsync();
    Task SaveVaultAsync();
    Task ChangeMasterPasswordAsync(string oldPassword, string newPassword);
    Task<bool> SyncWithCloudAsync();
    Task SaveSettingsAsync();
    void DetectCommonCloudFolders();
}

public class VaultService : IVaultService
{
    private readonly ICryptoService _cryptoService;
    private string _activeMasterPassword = string.Empty;

    public bool IsVaultCreated => File.Exists(LocalVaultPath);
    public bool IsUnlocked => CurrentContainer != null && !string.IsNullOrEmpty(_activeMasterPassword);
    public VaultContainer? CurrentContainer { get; private set; }
    public AppSettings Settings { get; private set; } = new();

    public string LocalVaultPath => string.IsNullOrWhiteSpace(Settings.LocalVaultPath)
        ? GetDefaultVaultPath()
        : Settings.LocalVaultPath;

    public VaultService(ICryptoService cryptoService)
    {
        _cryptoService = cryptoService;
    }

    private static string GetAppDataDir()
    {
        string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "STORM KEYS");
        if (!Directory.Exists(dir))
            Directory.CreateDirectory(dir);
        return dir;
    }

    private static string GetDefaultVaultPath() => Path.Combine(GetAppDataDir(), "vault.stormdb");
    private static string GetSettingsPath() => Path.Combine(GetAppDataDir(), "settings.json");

    public async Task InitializeAsync()
    {
        await LoadSettingsAsync();
        DetectCommonCloudFolders();
    }

    private async Task LoadSettingsAsync()
    {
        string path = GetSettingsPath();
        if (File.Exists(path))
        {
            try
            {
                string json = await File.ReadAllTextAsync(path);
                Settings = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            }
            catch
            {
                Settings = new AppSettings();
            }
        }
        else
        {
            Settings = new AppSettings
            {
                LocalVaultPath = GetDefaultVaultPath()
            };
            await SaveSettingsAsync();
        }
    }

    public async Task SaveSettingsAsync()
    {
        string path = GetSettingsPath();
        string json = JsonSerializer.Serialize(Settings, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(path, json);
    }

    public void DetectCommonCloudFolders()
    {
        if (!string.IsNullOrWhiteSpace(Settings.CloudSyncFolderPath) && Directory.Exists(Settings.CloudSyncFolderPath))
            return;

        // Автопоиск OneDrive
        string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string oneDrive = Path.Combine(userProfile, "OneDrive");
        if (Directory.Exists(oneDrive))
        {
            string stormCloud = Path.Combine(oneDrive, "STORM KEYS");
            Settings.CloudSyncFolderPath = stormCloud;
            return;
        }

        // Автопоиск Google Drive
        string[] gDriveLetters = { "G", "H", "I" };
        foreach (var letter in gDriveLetters)
        {
            string gDrivePath = $@"{letter}:\Мой диск";
            if (!Directory.Exists(gDrivePath))
                gDrivePath = $@"{letter}:\My Drive";

            if (Directory.Exists(gDrivePath))
            {
                Settings.CloudSyncFolderPath = Path.Combine(gDrivePath, "STORM KEYS");
                return;
            }
        }
    }

    public async Task CreateVaultAsync(string masterPassword)
    {
        if (string.IsNullOrWhiteSpace(masterPassword))
            throw new ArgumentException("Мастер-пароль не может быть пустым.");

        CurrentContainer = new VaultContainer
        {
            Metadata = new VaultMetadata
            {
                AppName = "STORM KEYS",
                SchemaVersion = "1.0.0",
                CreatedAt = DateTimeOffset.UtcNow,
                LastModifiedAt = DateTimeOffset.UtcNow,
                DeviceName = Environment.MachineName,
                EntryCount = 0
            },
            Entries = new List<KeyEntry>()
        };

        _activeMasterPassword = masterPassword;
        await SaveVaultAsync();
    }

    public async Task<bool> UnlockVaultAsync(string masterPassword)
    {
        if (!IsVaultCreated)
            return false;

        try
        {
            byte[] encrypted = await File.ReadAllBytesAsync(LocalVaultPath);
            var container = await _cryptoService.DecryptVaultAsync(encrypted, masterPassword);
            CurrentContainer = container;
            _activeMasterPassword = masterPassword;
            return true;
        }
        catch
        {
            return false;
        }
    }

    public Task LockVaultAsync()
    {
        _activeMasterPassword = string.Empty;
        CurrentContainer = null;
        return Task.CompletedTask;
    }

    public async Task SaveVaultAsync()
    {
        if (CurrentContainer == null || string.IsNullOrEmpty(_activeMasterPassword))
            throw new InvalidOperationException("Хранилище заблокировано или не инициализировано.");

        CurrentContainer.Metadata.LastModifiedAt = DateTimeOffset.UtcNow;
        CurrentContainer.Metadata.EntryCount = CurrentContainer.Entries.Count;

        byte[] encryptedBytes = await _cryptoService.EncryptVaultAsync(CurrentContainer, _activeMasterPassword);

        // Атомарное сохранение локального файла
        string targetPath = LocalVaultPath;
        string tempPath = targetPath + ".tmp";
        string backupPath = targetPath + ".bak";

        string? dir = Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        await File.WriteAllBytesAsync(tempPath, encryptedBytes);

        if (File.Exists(targetPath))
        {
            try
            {
                File.Copy(targetPath, backupPath, overwrite: true);
            }
            catch { }
        }

        File.Move(tempPath, targetPath, overwrite: true);

        // Синхронизация с облачной папкой, если включена
        if (Settings.CloudSyncEnabled && !string.IsNullOrWhiteSpace(Settings.CloudSyncFolderPath))
        {
            try
            {
                if (!Directory.Exists(Settings.CloudSyncFolderPath))
                    Directory.CreateDirectory(Settings.CloudSyncFolderPath);

                string cloudVaultFile = Path.Combine(Settings.CloudSyncFolderPath, "vault.stormdb");
                string cloudBackupFile = Path.Combine(Settings.CloudSyncFolderPath, "vault.backup.stormdb");

                if (File.Exists(cloudVaultFile))
                {
                    try { File.Copy(cloudVaultFile, cloudBackupFile, overwrite: true); } catch { }
                }

                await File.WriteAllBytesAsync(cloudVaultFile, encryptedBytes);
                Settings.LastCloudSyncTime = DateTimeOffset.UtcNow;
                await SaveSettingsAsync();
            }
            catch
            {
                // Ошибка облачного диска не должна ломать локальное сохранение
            }
        }
    }

    public async Task ChangeMasterPasswordAsync(string oldPassword, string newPassword)
    {
        if (CurrentContainer == null)
            throw new InvalidOperationException("Хранилище не открыто.");

        if (_activeMasterPassword != oldPassword)
            throw new ArgumentException("Текущий мастер-пароль указан неверно.");

        if (string.IsNullOrWhiteSpace(newPassword))
            throw new ArgumentException("Новый мастер-пароль не может быть пустым.");

        _activeMasterPassword = newPassword;
        await SaveVaultAsync();
    }

    public async Task<bool> SyncWithCloudAsync()
    {
        if (CurrentContainer == null || string.IsNullOrEmpty(_activeMasterPassword))
            return false;

        if (string.IsNullOrWhiteSpace(Settings.CloudSyncFolderPath) || !Directory.Exists(Settings.CloudSyncFolderPath))
            return false;

        string cloudVaultFile = Path.Combine(Settings.CloudSyncFolderPath, "vault.stormdb");
        if (!File.Exists(cloudVaultFile))
        {
            // Облачного файла еще нет — загружаем текущее состояние в облако
            await SaveVaultAsync();
            return true;
        }

        try
        {
            // Читаем облачный файл и проверяем дату изменения
            byte[] cloudBytes = await File.ReadAllBytesAsync(cloudVaultFile);
            var cloudContainer = await _cryptoService.DecryptVaultAsync(cloudBytes, _activeMasterPassword);

            if (cloudContainer.Metadata.LastModifiedAt > CurrentContainer.Metadata.LastModifiedAt)
            {
                // Облачная версия новее! Объединяем записи без потерь
                var localDict = new Dictionary<Guid, KeyEntry>();
                foreach (var entry in CurrentContainer.Entries)
                    localDict[entry.Id] = entry;

                foreach (var cloudEntry in cloudContainer.Entries)
                {
                    if (localDict.TryGetValue(cloudEntry.Id, out var localEntry))
                    {
                        if (cloudEntry.ModifiedAt > localEntry.ModifiedAt)
                            localDict[cloudEntry.Id] = cloudEntry;
                    }
                    else
                    {
                        localDict[cloudEntry.Id] = cloudEntry;
                    }
                }

                CurrentContainer.Entries = new List<KeyEntry>(localDict.Values);
                CurrentContainer.Metadata.LastModifiedAt = DateTimeOffset.UtcNow;
                await SaveVaultAsync();
            }
            else
            {
                // Локальная версия новее или равна — обновляем облако
                await SaveVaultAsync();
            }

            Settings.LastCloudSyncTime = DateTimeOffset.UtcNow;
            await SaveSettingsAsync();
            return true;
        }
        catch
        {
            return false;
        }
    }
}
