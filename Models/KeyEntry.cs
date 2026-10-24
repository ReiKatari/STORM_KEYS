using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;
using StormKeys.Helpers;

namespace StormKeys.Models;

public enum KeyCategory
{
    Account = 0,        // Логины и пароли сайтов / браузеров / приложений
    License = 1,        // Лицензионные ключи ПО, Windows, игр, IDE
    ApiKey = 2,         // API токены, секреты, Bearer, Webhook
    Certificate = 3,    // SSL/TLS сертификаты, SSH, PGP ключи
    Server = 4,         // Доступы к серверам, базам данных, RDP, VPN
    Other = 5,          // Защищённые заметки, пин-коды, документы
    BankCard = 6,       // Банковские карты, CVV, платёжные реквизиты
    Wifi = 7,           // Сети Wi-Fi, роутеры, точки доступа
    Crypto = 8          // Криптокошельки, Seed-фразы, приватные ключи
}

public class PasswordHistoryItem
{
    public string Password { get; set; } = string.Empty;
    public DateTimeOffset ChangedAt { get; set; } = DateTimeOffset.Now;
}

public class RecoveryCodeItem
{
    public string Code { get; set; } = string.Empty;
    public bool IsUsed { get; set; }
}

public class EncryptedAttachment
{
    public string FileName { get; set; } = string.Empty;
    public string ContentBase64 { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public DateTimeOffset AttachedAt { get; set; } = DateTimeOffset.UtcNow;
    [JsonIgnore]
    public string Base64Data { get => ContentBase64; set => ContentBase64 = value; }
    [JsonIgnore]
    public DateTimeOffset AddedAt { get => AttachedAt; set => AttachedAt = value; }
}

public class IconOptionItem
{
    public string Glyph { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}

public partial class KeyEntry : ObservableObject
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = string.Empty;

    private KeyCategory _category = KeyCategory.Account;
    public KeyCategory Category
    {
        get => _category;
        set
        {
            if (SetProperty(ref _category, value))
            {
                OnPropertyChanged(nameof(CategoryDisplayName));
                OnPropertyChanged(nameof(CategoryGlyph));
                OnPropertyChanged(nameof(EffectiveGlyph));
            }
        }
    }

    private string _customIconGlyph = string.Empty;
    public string CustomIconGlyph
    {
        get => _customIconGlyph;
        set
        {
            if (SetProperty(ref _customIconGlyph, value ?? string.Empty))
            {
                OnPropertyChanged(nameof(EffectiveGlyph));
            }
        }
    }

    public string UsernameOrEmail { get; set; } = string.Empty;

    private string _secretValue = string.Empty;
    public string SecretValue
    {
        get => _secretValue;
        set
        {
            if (SetProperty(ref _secretValue, value))
            {
                OnPropertyChanged(nameof(MaskedSecret));
                OnPropertyChanged(nameof(DisplayedSecret));
                OnPropertyChanged(nameof(PasswordStrengthLabel));
                OnPropertyChanged(nameof(PasswordStrengthColor));
                OnPropertyChanged(nameof(EntropyBits));
                OnPropertyChanged(nameof(CrackTimeText));
            }
        }
    }

    private bool _isSecretRevealed;
    [JsonIgnore]
    public bool IsSecretRevealed
    {
        get => _isSecretRevealed;
        set
        {
            if (SetProperty(ref _isSecretRevealed, value))
            {
                OnPropertyChanged(nameof(DisplayedSecret));
                OnPropertyChanged(nameof(PeekGlyph));
            }
        }
    }

    [JsonIgnore]
    public string DisplayedSecret => IsSecretRevealed ? SecretValue : MaskedSecret;

    [JsonIgnore]
    public string PeekGlyph => IsSecretRevealed ? "\uED1A" : "\uE7B3";

    private string _targetAppOrUrl = string.Empty;
    public string TargetAppOrUrl
    {
        get => _targetAppOrUrl;
        set
        {
            if (SetProperty(ref _targetAppOrUrl, value))
            {
                OnPropertyChanged(nameof(HasWebsiteUrl));
                OnPropertyChanged(nameof(HasLaunchTarget));
                OnPropertyChanged(nameof(HasLaunchTargetVisibility));
                OnPropertyChanged(nameof(IsAppBinding));
                OnPropertyChanged(nameof(LaunchTargetGlyph));
                OnPropertyChanged(nameof(LaunchTargetTooltip));
                OnPropertyChanged(nameof(WebsiteLaunchUri));
            }
        }
    }

    public DateTimeOffset? ExpirationDate { get; set; }
    public string Notes { get; set; } = string.Empty;

    private List<string> _tags = new();
    public List<string> Tags
    {
        get => _tags;
        set
        {
            if (SetProperty(ref _tags, value ?? new()))
            {
                OnPropertyChanged(nameof(TagsDisplayText));
                OnPropertyChanged(nameof(TagsVisibility));
            }
        }
    }

    [JsonIgnore]
    public string TagsDisplayText => (Tags != null && Tags.Count > 0)
        ? string.Join(" ", Tags.Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => "#" + t.Trim().TrimStart('#')))
        : string.Empty;

    [JsonIgnore]
    public Visibility TagsVisibility => !string.IsNullOrWhiteSpace(TagsDisplayText) ? Visibility.Visible : Visibility.Collapsed;

    // Избранное / Закреплено вверху
    private bool _isFavorite;
    public bool IsFavorite
    {
        get => _isFavorite;
        set
        {
            if (SetProperty(ref _isFavorite, value))
            {
                OnPropertyChanged(nameof(FavoriteGlyph));
            }
        }
    }

    [JsonIgnore]
    public string FavoriteGlyph => IsFavorite ? "\uE735" : "\uE734";

    // Выбор для массовых операций
    private bool _isSelected;
    [JsonIgnore]
    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    // Пользовательские атрибуты (PIN, Port, SSH User, CVV, SSID, etc.)
    public Dictionary<string, string> CustomFields { get; set; } = new();

    // История предыдущих паролей
    public List<PasswordHistoryItem> PasswordHistory { get; set; } = new();

    private int _usageCount;
    public int UsageCount
    {
        get => _usageCount;
        set
        {
            if (SetProperty(ref _usageCount, value))
            {
                OnPropertyChanged(nameof(UsageBadgeText));
                OnPropertyChanged(nameof(UsageBadgeVisibility));
            }
        }
    }

    public DateTimeOffset? LastUsedAt { get; set; }

    // Настраиваемый макрос авто-ввода (по умолчанию: {USER}{TAB}{PASS}{ENTER})
    public string AutoTypeSequence { get; set; } = "{USER}{TAB}{PASS}{ENTER}";
    [JsonIgnore]
    public string EffectiveAutoTypeSequence => string.IsNullOrWhiteSpace(AutoTypeSequence) ? "{USER}{TAB}{PASS}{ENTER}" : AutoTypeSequence;

    // Одноразовые резервные коды восстановления 2FA (с учётом использованных)
    public List<RecoveryCodeItem> RecoveryCodes { get; set; } = new();

    [JsonIgnore]
    public int ActiveRecoveryCodesCount => RecoveryCodes?.Count(c => !c.IsUsed && !string.IsNullOrWhiteSpace(c.Code)) ?? 0;

    [JsonIgnore]
    public Visibility HasRecoveryCodesVisibility => ActiveRecoveryCodesCount > 0 ? Visibility.Visible : Visibility.Collapsed;

    [JsonIgnore]
    public string RecoveryCodesBadgeText => $"Резерв 2FA: {ActiveRecoveryCodesCount}";

    [JsonIgnore]
    public int RecoveryCodesCount => ActiveRecoveryCodesCount;
    public string? ConsumeOneRecoveryCode() => ConsumeNextRecoveryCode();
    public string? ConsumeNextRecoveryCode()
    {
        if (RecoveryCodes == null) return null;
        var next = RecoveryCodes.FirstOrDefault(c => !c.IsUsed && !string.IsNullOrWhiteSpace(c.Code));
        if (next == null) return null;
        next.IsUsed = true;
        OnPropertyChanged(nameof(ActiveRecoveryCodesCount));
        OnPropertyChanged(nameof(HasRecoveryCodesVisibility));
        OnPropertyChanged(nameof(RecoveryCodesBadgeText));
        return next.Code.Trim();
    }

    // Зашифрованные микро-вложения внутри карточки (.lic, .reg, .pem, .ppk, .cer, .txt)
    public List<EncryptedAttachment> Attachments { get; set; } = new();

    [JsonIgnore]
    public int AttachmentsCount => Attachments?.Count ?? 0;
    [JsonIgnore]
    public bool HasAttachments => AttachmentsCount > 0;
    [JsonIgnore]
    public string AttachmentsBadgeText => $"📎 {AttachmentsCount}";

    [JsonIgnore]
    public Visibility HasAttachmentsVisibility => AttachmentsCount > 0 ? Visibility.Visible : Visibility.Collapsed;

    [JsonIgnore]
    public string AttachmentsSummaryText => AttachmentsCount > 0
        ? $"📎 Вложений: {AttachmentsCount} ({string.Join(", ", Attachments!.Select(a => a.FileName))})"
        : "Вложений нет";

    // Корзина удалённых записей (хранение 30 дней перед окончательной очисткой)
    private bool _isDeleted;
    public bool IsDeleted
    {
        get => _isDeleted;
        set
        {
            if (SetProperty(ref _isDeleted, value))
            {
                OnPropertyChanged(nameof(DeletedBadgeVisibility));
            }
        }
    }

    public DateTimeOffset? DeletedAt { get; set; }

    [JsonIgnore]
    public Visibility DeletedBadgeVisibility => IsDeleted ? Visibility.Visible : Visibility.Collapsed;
    [JsonIgnore]
    public Visibility IsDeletedVisibility => DeletedBadgeVisibility;
    [JsonIgnore]
    public Visibility IsBankCardVisibility => Category == KeyCategory.BankCard ? Visibility.Visible : Visibility.Collapsed;
    [JsonIgnore]
    public Visibility IsWifiVisibility => HasWifiQuickActionsVisibility;

    // Умные быстрые поля для банковских карт (Срок ММ/ГГ, CVV, PIN)
    [JsonIgnore]
    public string CardExpiryValue => GetCustomFieldValue("Срок", "Expiry", "ММ/ГГ", "Valid");

    [JsonIgnore]
    public string CardCvvValue => GetCustomFieldValue("CVV", "CVC", "Код");

    [JsonIgnore]
    public string CardPinValue => GetCustomFieldValue("PIN", "ПИН");

    [JsonIgnore]
    public Visibility HasBankCardQuickActionsVisibility =>
        (Category == KeyCategory.BankCard && (!string.IsNullOrEmpty(CardCvvValue) || !string.IsNullOrEmpty(CardPinValue) || !string.IsNullOrEmpty(CardExpiryValue)))
            ? Visibility.Visible
            : Visibility.Collapsed;

    // Умные быстрые поля для Wi-Fi (SSID + генерация строки QR-кода WIFI:S:...;T:WPA;P:...;;)
    [JsonIgnore]
    public string WifiSsidValue
    {
        get
        {
            string fromField = GetCustomFieldValue("SSID", "Сеть", "Имя сети");
            if (!string.IsNullOrWhiteSpace(fromField)) return fromField;
            if (!string.IsNullOrWhiteSpace(UsernameOrEmail)) return UsernameOrEmail.Trim();
            return Title.Trim();
        }
    }

    [JsonIgnore]
    public Visibility HasWifiQuickActionsVisibility => Category == KeyCategory.Wifi ? Visibility.Visible : Visibility.Collapsed;

    public string GetWifiQrPayload()
    {
        string ssid = WifiSsidValue.Replace(";", "\\;").Replace(":", "\\:");
        string pass = (SecretValue ?? string.Empty).Replace(";", "\\;").Replace(":", "\\:");
        string auth = GetCustomFieldValue("Защита", "Security", "Auth");
        string type = (!string.IsNullOrWhiteSpace(auth) && auth.Contains("WEP", StringComparison.OrdinalIgnoreCase)) ? "WEP" : "WPA";
        if (string.IsNullOrEmpty(pass)) type = "nopass";
        return $"WIFI:S:{ssid};T:{type};P:{pass};;";
    }

    private string GetCustomFieldValue(params string[] keyFragments)
    {
        if (CustomFields == null || CustomFields.Count == 0) return string.Empty;
        foreach (var kv in CustomFields)
        {
            foreach (var frag in keyFragments)
            {
                if (kv.Key.Contains(frag, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(kv.Value))
                    return kv.Value.Trim();
            }
        }
        return string.Empty;
    }

    /// <summary>
    /// Проверяет, подходит ли запись к текущему активному окну браузера или программы.
    /// </summary>
    public bool MatchesActiveWindow(string? windowTitle, string? processName)
    {
        if (IsDeleted) return false;
        if (string.IsNullOrWhiteSpace(windowTitle) && string.IsNullOrWhiteSpace(processName)) return false;

        string winTitle = (windowTitle ?? string.Empty).ToLowerInvariant();
        string proc = (processName ?? string.Empty).ToLowerInvariant();

        if (!string.IsNullOrWhiteSpace(Title) && Title.Length >= 3 && winTitle.Contains(Title.Trim().ToLowerInvariant()))
            return true;

        if (!string.IsNullOrWhiteSpace(TargetAppOrUrl))
        {
            string target = TargetAppOrUrl.Trim().ToLowerInvariant();
            string exeName = Path.GetFileNameWithoutExtension(target);
            if (!string.IsNullOrWhiteSpace(exeName) && exeName.Length >= 3)
            {
                if (proc.Equals(exeName, StringComparison.OrdinalIgnoreCase) || winTitle.Contains(exeName))
                    return true;
            }

            if (WebsiteLaunchUri != null && !string.IsNullOrWhiteSpace(WebsiteLaunchUri.Host))
            {
                string host = WebsiteLaunchUri.Host.ToLowerInvariant();
                if (host.StartsWith("www.")) host = host[4..];
                int dotIdx = host.IndexOf('.');
                string domainCore = dotIdx > 0 ? host[..dotIdx] : host;
                if (domainCore.Length >= 3 && winTitle.Contains(domainCore))
                    return true;
            }
        }

        return false;
    }

    // Двухфакторная аутентификация (TOTP / 2FA)
    private string _totpSecret = string.Empty;
    public string TotpSecret
    {
        get => _totpSecret;
        set
        {
            if (SetProperty(ref _totpSecret, value))
            {
                OnPropertyChanged(nameof(HasTotp));
                OnPropertyChanged(nameof(TotpVisibility));
            }
        }
    }

    [JsonIgnore]
    public bool HasTotp => !string.IsNullOrWhiteSpace(TotpSecret);

    [JsonIgnore]
    public Visibility TotpVisibility => HasTotp ? Visibility.Visible : Visibility.Collapsed;

    [JsonIgnore]
    public bool IsValidLuhnCardNumber => Category == KeyCategory.BankCard && Helpers.PasswordGenerator.ValidateLuhnChecksum(SecretValue);

    [JsonIgnore]
    public string CardBrandAndLuhnText
    {
        get
        {
            if (Category != KeyCategory.BankCard || string.IsNullOrWhiteSpace(SecretValue))
                return string.Empty;
            var brand = Helpers.PasswordGenerator.DetectCardBrand(SecretValue);
            var valid = Helpers.PasswordGenerator.ValidateLuhnChecksum(SecretValue);
            return valid ? $"💳 {brand} • Luhn OK" : $"💳 {brand} • Проверьте номер";
        }
    }

    [JsonIgnore]
    public Visibility CardBrandBadgeVisibility => (Category == KeyCategory.BankCard && !string.IsNullOrWhiteSpace(SecretValue))
        ? Visibility.Visible
        : Visibility.Collapsed;

    [JsonIgnore]
    public bool IsMissing2FaForCriticalService
    {
        get
        {
            if (HasTotp || Category is KeyCategory.BankCard or KeyCategory.License or KeyCategory.Wifi)
                return false;
            var hay = $"{Title} {TargetAppOrUrl}".ToLowerInvariant();
            string[] critical = ["google", "gmail", "github", "gitlab", "steam", "microsoft", "outlook", "live.com", "apple", "icloud", "telegram", "discord", "binance", "bybit", "coinbase", "aws", "amazon", "cloudflare", "gosuslugi", "госуслуги", "яндекс", "yandex", "vk.com", "вконтакте", "sber", "сбер", "tbank", "тинькофф", "paypal", "openai", "anthropic"];
            return critical.Any(c => hay.Contains(c));
        }
    }

    [JsonIgnore]
    public Visibility Missing2FaBadgeVisibility => IsMissing2FaForCriticalService ? Visibility.Visible : Visibility.Collapsed;

    [JsonIgnore]
    public string UsageBadgeText => UsageCount > 0 ? $"⚡ {UsageCount}" : string.Empty;

    [JsonIgnore]
    public Visibility UsageBadgeVisibility => UsageCount > 0 ? Visibility.Visible : Visibility.Collapsed;

    [ObservableProperty]
    [property: JsonIgnore]
    private string _currentTotpCode = string.Empty;

    [ObservableProperty]
    [property: JsonIgnore]
    private int _totpSecondsRemaining = 30;

    [ObservableProperty]
    [property: JsonIgnore]
    private double _totpProgress = 1.0;

    partial void OnTotpSecondsRemainingChanged(int value)
    {
        OnPropertyChanged(nameof(TotpCountdownText));
        OnPropertyChanged(nameof(TotpTimerColor));
    }

    partial void OnCurrentTotpCodeChanged(string value)
    {
        OnPropertyChanged(nameof(FormattedTotpCode));
    }

    [JsonIgnore]
    public string FormattedTotpCode => string.IsNullOrEmpty(CurrentTotpCode)
        ? "--- ---"
        : (CurrentTotpCode.Length == 6 ? $"{CurrentTotpCode[..3]} {CurrentTotpCode[3..]}" : CurrentTotpCode);

    [JsonIgnore]
    public string TotpCountdownText => $"{TotpSecondsRemaining}s";

    [JsonIgnore]
    public string TotpTimerColor => TotpSecondsRemaining switch
    {
        <= 5 => "#D13438",  // Красный
        <= 10 => "#FF8C00", // Оранжевый
        _ => "#107C41"      // Зелёный
    };

    // Аудит безопасности (Have I Been Pwned)
    [ObservableProperty]
    private int _pwnedBreachCount = -1;

    [ObservableProperty]
    private DateTimeOffset? _pwnedLastCheckedAt;

    partial void OnPwnedBreachCountChanged(int value)
    {
        OnPropertyChanged(nameof(IsPwnedChecked));
        OnPropertyChanged(nameof(IsPwnedBreached));
        OnPropertyChanged(nameof(PwnedStatusText));
        OnPropertyChanged(nameof(PwnedStatusColor));
        OnPropertyChanged(nameof(PwnedBreachedVisibility));
        OnPropertyChanged(nameof(PwnedSafeVisibility));
    }

    partial void OnPwnedLastCheckedAtChanged(DateTimeOffset? value)
    {
        OnPropertyChanged(nameof(PwnedSafeVisibility));
    }

    [JsonIgnore]
    public bool IsPwnedChecked => PwnedBreachCount >= 0;

    [JsonIgnore]
    public bool IsPwnedBreached => PwnedBreachCount > 0;

    [JsonIgnore]
    public Visibility PwnedBreachedVisibility => PwnedBreachCount > 0 ? Visibility.Visible : Visibility.Collapsed;

    [JsonIgnore]
    public Visibility PwnedSafeVisibility => (PwnedBreachCount == 0 && PwnedLastCheckedAt.HasValue) ? Visibility.Visible : Visibility.Collapsed;

    [JsonIgnore]
    public string PwnedStatusText => PwnedBreachCount < 0
        ? "Не проверялся"
        : (PwnedBreachCount == 0 ? "0 утечек (Безопасен)" : $"Утечка: {PwnedBreachCount:N0} раз!");

    [JsonIgnore]
    public string PwnedStatusColor => PwnedBreachCount < 0
        ? "#888888"
        : (PwnedBreachCount == 0 ? "#107C41" : "#D13438");

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ModifiedAt { get; set; } = DateTimeOffset.UtcNow;

    // Свойства для уведомлений
    public bool Notified30Days { get; set; }
    public bool Notified7Days { get; set; }
    public bool Notified1Day { get; set; }
    public bool NotifiedExpired { get; set; }

    [JsonIgnore]
    public bool HasExpiration => ExpirationDate.HasValue;

    [JsonIgnore]
    public int? DaysRemaining
    {
        get
        {
            if (!ExpirationDate.HasValue) return null;
            var diff = (ExpirationDate.Value.Date - DateTimeOffset.Now.Date).TotalDays;
            return (int)diff;
        }
    }

    [JsonIgnore]
    public bool IsExpired => ExpirationDate.HasValue && DaysRemaining < 0;

    [JsonIgnore]
    public bool IsExpiringSoon => ExpirationDate.HasValue && !IsExpired && DaysRemaining <= 30;

    [JsonIgnore]
    public bool IsCriticalExpiring => ExpirationDate.HasValue && !IsExpired && DaysRemaining <= 7;

    [JsonIgnore]
    public string ExpirationStatusText
    {
        get
        {
            if (!ExpirationDate.HasValue)
                return "Бессрочно";

            var days = DaysRemaining.GetValueOrDefault();
            if (days < 0)
                return $"Истёк {Math.Abs(days)} дн. назад ({ExpirationDate.Value:dd.MM.yyyy})";
            if (days == 0)
                return $"Истекает сегодня! ({ExpirationDate.Value:dd.MM.yyyy})";
            if (days == 1)
                return $"Истекает завтра ({ExpirationDate.Value:dd.MM.yyyy})";
            if (days <= 30)
                return $"Истекает через {days} дн. ({ExpirationDate.Value:dd.MM.yyyy})";

            return $"Действует до {ExpirationDate.Value:dd.MM.yyyy}";
        }
    }

    [JsonIgnore]
    public string StatusBadgeColor
    {
        get
        {
            if (!ExpirationDate.HasValue)
                return "#3A3D40";
            if (IsExpired)
                return "#D13438";
            if (IsCriticalExpiring)
                return "#FF8C00";
            if (IsExpiringSoon)
                return "#FCE100";
            return "#107C41";
        }
    }

    [JsonIgnore]
    public string CategoryDisplayName => Category switch
    {
        KeyCategory.Account => "Учетная запись",
        KeyCategory.License => "Лицензия ПО",
        KeyCategory.ApiKey => "API Ключ / Токен",
        KeyCategory.Certificate => "Сертификат / SSH",
        KeyCategory.Server => "Сервер / БД / RDP",
        KeyCategory.BankCard => "Банковская карта",
        KeyCategory.Wifi => "Сеть Wi-Fi",
        KeyCategory.Crypto => "Крипто / Seed-фраза",
        _ => "Заметка / PIN"
    };

    [JsonIgnore]
    public string CategoryGlyph => Category switch
    {
        KeyCategory.Account => "\uE77B",     // Contact / Person
        KeyCategory.License => "\uE8A7",     // Certificate / Ribbon
        KeyCategory.ApiKey => "\uE943",      // Code / Key
        KeyCategory.Certificate => "\uE72E", // Lock / Shield
        KeyCategory.Server => "\uEDA2",      // Cloud / Server
        KeyCategory.BankCard => "\uE8C7",    // Payment Card
        KeyCategory.Wifi => "\uE701",        // Wi-Fi
        KeyCategory.Crypto => "\uE8AC",      // Safe / Crypto
        _ => "\uE8D7"                        // Key / Other
    };

    [JsonIgnore]
    public string EffectiveGlyph => !string.IsNullOrWhiteSpace(CustomIconGlyph) ? CustomIconGlyph : CategoryGlyph;

    [JsonIgnore]
    public string MaskedSecret => string.IsNullOrEmpty(SecretValue)
        ? string.Empty
        : new string('•', Math.Min(SecretValue.Length, 12));

    [JsonIgnore]
    public bool IsAppBinding
    {
        get
        {
            if (string.IsNullOrWhiteSpace(TargetAppOrUrl)) return false;
            string t = TargetAppOrUrl.Trim();
            return t.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
                   t.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) ||
                   t.EndsWith(".rdp", StringComparison.OrdinalIgnoreCase) ||
                   t.EndsWith(".bat", StringComparison.OrdinalIgnoreCase) ||
                   t.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase) ||
                   (t.Length >= 3 && char.IsLetter(t[0]) && t[1] == ':' && (t[2] == '\\' || t[2] == '/')) ||
                   t.StartsWith(@"\\", StringComparison.OrdinalIgnoreCase);
        }
    }

    [JsonIgnore]
    public bool HasWebsiteUrl => !string.IsNullOrWhiteSpace(TargetAppOrUrl) && !IsAppBinding;

    [JsonIgnore]
    public bool HasLaunchTarget => !string.IsNullOrWhiteSpace(TargetAppOrUrl);

    [JsonIgnore]
    public Visibility HasLaunchTargetVisibility => HasLaunchTarget ? Visibility.Visible : Visibility.Collapsed;

    [JsonIgnore]
    public string LaunchTargetGlyph => IsAppBinding ? "\uE756" : "\uE774";

    [JsonIgnore]
    public string LaunchTargetTooltip => IsAppBinding
        ? $"Запустить привязанную программу ({Path.GetFileName(TargetAppOrUrl)})"
        : $"Открыть привязанный сайт / URI ({TargetAppOrUrl})";

    [JsonIgnore]
    public Uri? WebsiteLaunchUri
    {
        get
        {
            if (string.IsNullOrWhiteSpace(TargetAppOrUrl) || IsAppBinding)
                return null;

            string raw = TargetAppOrUrl.Trim();
            if (Uri.TryCreate(raw, UriKind.Absolute, out var uri))
                return uri;

            if (raw.Contains('.') && !raw.Contains(' ') && Uri.TryCreate("https://" + raw, UriKind.Absolute, out var httpsUri))
                return httpsUri;

            return null;
        }
    }

    [JsonIgnore]
    public string PasswordStrengthLabel
    {
        get
        {
            if (string.IsNullOrEmpty(SecretValue)) return "—";
            var eval = PasswordGenerator.EvaluateStrength(SecretValue);
            return eval.Label;
        }
    }

    [JsonIgnore]
    public string PasswordStrengthColor
    {
        get
        {
            if (string.IsNullOrEmpty(SecretValue)) return "#7A7A7A";
            var eval = PasswordGenerator.EvaluateStrength(SecretValue);
            return eval.ColorHex;
        }
    }

    [JsonIgnore]
    public double EntropyBits => PasswordGenerator.CalculateEntropyBits(SecretValue);

    [JsonIgnore]
    public string CrackTimeText => PasswordGenerator.EstimateCrackTime(SecretValue);

    public KeyEntry Clone()
    {
        return new KeyEntry
        {
            Id = Guid.NewGuid(),
            Title = $"{Title} (Копия)",
            Category = Category,
            CustomIconGlyph = CustomIconGlyph,
            UsernameOrEmail = UsernameOrEmail,
            SecretValue = SecretValue,
            TargetAppOrUrl = TargetAppOrUrl,
            ExpirationDate = ExpirationDate,
            Notes = Notes,
            TotpSecret = TotpSecret,
            AutoTypeSequence = AutoTypeSequence,
            IsFavorite = IsFavorite,
            Tags = new List<string>(Tags ?? new()),
            CustomFields = new Dictionary<string, string>(CustomFields ?? new()),
            RecoveryCodes = RecoveryCodes?.Select(r => new RecoveryCodeItem { Code = r.Code, IsUsed = r.IsUsed }).ToList() ?? new(),
            Attachments = Attachments?.Select(a => new EncryptedAttachment
            {
                FileName = a.FileName,
                ContentBase64 = a.ContentBase64,
                SizeBytes = a.SizeBytes,
                AttachedAt = a.AttachedAt
            }).ToList() ?? new(),
            CreatedAt = DateTimeOffset.UtcNow,
            ModifiedAt = DateTimeOffset.UtcNow
        };
    }
}
