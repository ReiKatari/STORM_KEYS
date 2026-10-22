using System;
using System.Collections.Generic;

namespace StormKeys.Models;

public class VaultMetadata
{
    public string AppName { get; set; } = "STORM KEYS";
    public string SchemaVersion { get; set; } = "1.0.0";
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
    public int AutoLockMinutes { get; set; } = 15; // 0 = disabled, 5, 15, 30
    public bool NotifyOn30Days { get; set; } = true;
    public bool NotifyOn7Days { get; set; } = true;
    public bool NotifyOn1Day { get; set; } = true;
    public bool ClearClipboardAfterSeconds { get; set; } = true;
    public int ClipboardClearTimeout { get; set; } = 30; // 30 секунд
    public string Language { get; set; } = "ru"; // ru, en, de, fr, zh, ko, ja, es, it, pt, hi
    public string Theme { get; set; } = "MIDNIGHT"; // MIDNIGHT, NIGHT, DARK, DAY, FANTASY, CYBERPUNK, WARHAMMER 40K, ELEMENTAL, STORM
    public string Accent { get; set; } = "Violet"; // Violet (#8B5CF6), Cyan, Electric Blue, etc.
    public string FontFamily { get; set; } = "Segoe UI Variable"; // 10 fonts
    public DateTimeOffset LastCloudSyncTime { get; set; } = DateTimeOffset.MinValue;
}
