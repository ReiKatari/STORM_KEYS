using System;
using System.Collections.Generic;

namespace StormKeys.Models;

public class VaultMetadata
{
    public string AppName { get; set; } = "STORM KEYS";
    public string SchemaVersion { get; set; } = "1.0.2";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset LastModifiedAt { get; set; } = DateTimeOffset.UtcNow;
    public string DeviceName { get; set; } = Environment.MachineName;
    public int EntryCount { get; set; }
}

public class VaultContainer
{
    public VaultMetadata Metadata { get; set; } = new();
    public List<KeyEntry> Entries { get; set; } = new();
}

public class AppSettings
{
    public string LocalVaultPath { get; set; } = string.Empty;
    public string CloudSyncFolderPath { get; set; } = string.Empty;
    public bool CloudSyncEnabled { get; set; }
    public int AutoLockMinutes { get; set; } = 15; // 0 = disabled, 1, 5, 15, 30
    public bool NotifyOn30Days { get; set; } = true;
    public bool NotifyOn7Days { get; set; } = true;
    public bool NotifyOn1Day { get; set; } = true;
    public bool ClearClipboardAfterSeconds { get; set; } = true;
    public int ClipboardClearTimeout { get; set; } = 30; // 30 секунд
    public bool WindowsHelloEnabled { get; set; } = false;
    public bool IsCompactView { get; set; } = false; // Переключение плотности: Комфортный / Компактный
    public bool AutoBackupEnabled { get; set; } = true; // Автоматические резервные копии .stormbackup
    public string Language { get; set; } = "ru"; // 11 языков
    public string Theme { get; set; } = "MIDNIGHT"; // 9 тем
    public string Accent { get; set; } = "Violet"; // 13 акцентов
    public string FontFamily { get; set; } = "Segoe UI Variable"; // 10 шрифтов
    public DateTimeOffset LastCloudSyncTime { get; set; } = DateTimeOffset.MinValue;
}
