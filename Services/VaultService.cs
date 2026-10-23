using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using StormKeys.Models;

namespace StormKeys.Services;

public sealed record BackupSlotInfo(int SlotNumber, string FilePath, DateTimeOffset LastWriteTime, long SizeBytes);

public interface IVaultService
{
    bool IsVaultCreated { get; }
    bool IsUnlocked { get; }
    bool IsDecoySession { get; }
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
    Task<string> CreateBackupSnapshotAsync();
    List<BackupSlotInfo> GetBackupSlotsSummary();
    Task<bool> RestoreBackupSlotAsync(int slotNumber);
}

public class VaultService : IVaultService
{
    private readonly ICryptoService _cryptoService;
    private string _activeMasterPassword = string.Empty;

    public bool IsVaultCreated => File.Exists(LocalVaultPath);
    public bool IsUnlocked => CurrentContainer != null && (!string.IsNullOrEmpty(_activeMasterPassword) || IsDecoySession);
    public bool IsDecoySession { get; private set; }
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

        IsDecoySession = false;
        CurrentContainer = new VaultContainer
        {
            Metadata = new VaultMetadata
            {
                AppName = "STORM KEYS",
                SchemaVersion = "1.0.5",
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

        // 1. Проверка ложного пароля под принуждением (Duress / Decoy Vault)
        if (!string.IsNullOrWhiteSpace(Settings.DuressPasswordHash))
        {
            string inputHash = _cryptoService.ComputePasswordVerifierHash(masterPassword);
            if (string.Equals(inputHash, Settings.DuressPasswordHash, StringComparison.OrdinalIgnoreCase))
            {
                IsDecoySession = true;
                _activeMasterPassword = masterPassword;
                CurrentContainer = BuildDecoyVaultContainer();
                return true;
            }
        }

        try
        {
            byte[] encrypted = await File.ReadAllBytesAsync(LocalVaultPath);
            string effectivePassword = _cryptoService.CombinePasswordWithKeyfile(masterPassword, Settings.KeyfilePath);
            VaultContainer container;
            try
            {
                container = await _cryptoService.DecryptVaultAsync(encrypted, effectivePassword);
            }
            catch when (!string.IsNullOrWhiteSpace(Settings.KeyfilePath))
            {
                // Fallback, если ключевой файл только что привязан, а база ещё зашифрована чистым паролем
                container = await _cryptoService.DecryptVaultAsync(encrypted, masterPassword);
            }

            IsDecoySession = false;
            CurrentContainer = container;
            _activeMasterPassword = masterPassword;
            PurgeExpiredTrashEntries();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static VaultContainer BuildDecoyVaultContainer()
    {
        return new VaultContainer
        {
            Metadata = new VaultMetadata
            {
                AppName = "STORM KEYS",
                SchemaVersion = "1.0.5",
                CreatedAt = DateTimeOffset.UtcNow.AddDays(-45),
                LastModifiedAt = DateTimeOffset.UtcNow,
                DeviceName = Environment.MachineName,
                EntryCount = 2
            },
            Entries = new List<KeyEntry>
            {
                new KeyEntry
                {
                    Title = "Гостевой Wi-Fi",
                    Category = KeyCategory.Wifi,
                    UsernameOrEmail = "Home_Guest_5G",
                    SecretValue = "WelcomeGuest2026!",
                    Notes = "Гостевая точка доступа"
                },
                new KeyEntry
                {
                    Title = "Новостной портал",
                    Category = KeyCategory.Account,
                    UsernameOrEmail = "reader@example.com",
                    SecretValue = "NewsReader#9412",
                    TargetAppOrUrl = "https://example.com"
                }
            }
        };
    }

    private void PurgeExpiredTrashEntries()
    {
        if (CurrentContainer?.Entries == null) return;
        var cutoff = DateTimeOffset.UtcNow.AddDays(-30);
        CurrentContainer.Entries.RemoveAll(e => e.IsDeleted && e.DeletedAt.HasValue && e.DeletedAt.Value < cutoff);
    }

    public Task LockVaultAsync()
    {
        _activeMasterPassword = string.Empty;
        IsDecoySession = false;
        CurrentContainer = null;
        return Task.CompletedTask;
    }

    public async Task SaveVaultAsync()
    {
        if (CurrentContainer == null || string.IsNullOrEmpty(_activeMasterPassword))
            throw new InvalidOperationException("Хранилище заблокировано или не инициализировано.");

        // В режиме приманки (Duress / Decoy) никогда не перезаписываем реальный файл хранилища!
        if (IsDecoySession)
        {
            CurrentContainer.Metadata.LastModifiedAt = DateTimeOffset.UtcNow;
            CurrentContainer.Metadata.EntryCount = CurrentContainer.Entries.Count;
            return;
        }

        PurgeExpiredTrashEntries();
        CurrentContainer.Metadata.LastModifiedAt = DateTimeOffset.UtcNow;
        CurrentContainer.Metadata.EntryCount = CurrentContainer.Entries.Count;

        string effectivePassword = _cryptoService.CombinePasswordWithKeyfile(_activeMasterPassword, Settings.KeyfilePath);
        byte[] encryptedBytes = await _cryptoService.EncryptVaultAsync(CurrentContainer, effectivePassword);

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
                // Ротация 3 поколений резервных копий (.bak1, .bak2, .bak3)
                string bak1 = targetPath + ".bak1";
                string bak2 = targetPath + ".bak2";
                string bak3 = targetPath + ".bak3";
                if (File.Exists(bak2)) File.Copy(bak2, bak3, overwrite: true);
                if (File.Exists(bak1)) File.Copy(bak1, bak2, overwrite: true);
                File.Copy(targetPath, bak1, overwrite: true);
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
        if (CurrentContainer == null || string.IsNullOrEmpty(_activeMasterPassword) || IsDecoySession)
            return false;

        if (string.IsNullOrWhiteSpace(Settings.CloudSyncFolderPath) || !Directory.Exists(Settings.CloudSyncFolderPath))
            return false;

        string cloudVaultFile = Path.Combine(Settings.CloudSyncFolderPath, "vault.stormdb");
        if (!File.Exists(cloudVaultFile))
        {
            await SaveVaultAsync();
            return true;
        }

        try
        {
            byte[] cloudBytes = await File.ReadAllBytesAsync(cloudVaultFile);
            string effectivePassword = _cryptoService.CombinePasswordWithKeyfile(_activeMasterPassword, Settings.KeyfilePath);
            var cloudContainer = await _cryptoService.DecryptVaultAsync(cloudBytes, effectivePassword);

            if (cloudContainer.Metadata.LastModifiedAt > CurrentContainer.Metadata.LastModifiedAt)
            {
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

    public async Task<string> CreateBackupSnapshotAsync()
    {
        if (!File.Exists(LocalVaultPath))
            await SaveVaultAsync();

        string dir = Path.GetDirectoryName(LocalVaultPath) ?? GetAppDataDir();
        string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string snapshotPath = Path.Combine(dir, $"vault_snapshot_{stamp}.stormdb.bak");
        File.Copy(LocalVaultPath, snapshotPath, overwrite: true);
        return snapshotPath;
    }

    public List<BackupSlotInfo> GetBackupSlotsSummary()
    {
        var list = new List<BackupSlotInfo>();
        string targetPath = LocalVaultPath;
        for (int slot = 1; slot <= 3; slot++)
        {
            string bakPath = $"{targetPath}.bak{slot}";
            if (File.Exists(bakPath))
            {
                var fi = new FileInfo(bakPath);
                list.Add(new BackupSlotInfo(slot, bakPath, fi.LastWriteTime, fi.Length));
            }
        }
        return list;
    }

    public async Task<bool> RestoreBackupSlotAsync(int slotNumber)
    {
        if (slotNumber < 1 || slotNumber > 3 || string.IsNullOrEmpty(_activeMasterPassword) || IsDecoySession)
            return false;

        string bakPath = $"{LocalVaultPath}.bak{slotNumber}";
        if (!File.Exists(bakPath))
            return false;

        try
        {
            byte[] encrypted = await File.ReadAllBytesAsync(bakPath);
            string effectivePassword = _cryptoService.CombinePasswordWithKeyfile(_activeMasterPassword, Settings.KeyfilePath);
            var restoredContainer = await _cryptoService.DecryptVaultAsync(encrypted, effectivePassword);
            CurrentContainer = restoredContainer;
            await SaveVaultAsync();
            return true;
        }
        catch
        {
            return false;
        }
    }
}
