using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;

namespace StormKeys.Models;

public enum KeyCategory
{
    Account,        // Логины и пароли браузеров/приложений
    License,        // Лицензионные ключи ПО, Windows, IDE
    ApiKey,         // API токены, секреты, токены доступа
    Certificate,    // SSL/TLS сертификаты, SSH ключи
    Server,         // Доступы к серверам, базам данных, RDP
    Other           // Заметки, пин-коды, прочее
}

public partial class KeyEntry : ObservableObject
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = string.Empty;
    public KeyCategory Category { get; set; } = KeyCategory.Account;
    public string UsernameOrEmail { get; set; } = string.Empty;
    public string SecretValue { get; set; } = string.Empty;
    public string TargetAppOrUrl { get; set; } = string.Empty;
    public DateTimeOffset? ExpirationDate { get; set; }
    public string Notes { get; set; } = string.Empty;
    public List<string> Tags { get; set; } = new();

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
                return "#3A3D40"; // Нейтральный
            if (IsExpired)
                return "#D13438"; // Критический красный
            if (IsCriticalExpiring)
                return "#FF8C00"; // Тревожный оранжевый (<= 7 дней)
            if (IsExpiringSoon)
                return "#FCE100"; // Предупреждающий желтый (<= 30 дней)
            return "#107C41";     // Зеленый (активен)
        }
    }

    [JsonIgnore]
    public string CategoryDisplayName => Category switch
    {
        KeyCategory.Account => "Учетная запись",
        KeyCategory.License => "Лицензия ПО",
        KeyCategory.ApiKey => "API Ключ / Токен",
        KeyCategory.Certificate => "Сертификат / SSH",
        KeyCategory.Server => "Сервер / БД",
        _ => "Другое"
    };

    [JsonIgnore]
    public string CategoryGlyph => Category switch
    {
        KeyCategory.Account => "\uE77B",     // Contact / Person
        KeyCategory.License => "\uE8A7",     // Certificate / Ribbon
        KeyCategory.ApiKey => "\uE8D7",      // Code / Key
        KeyCategory.Certificate => "\uE72E", // Lock / Shield
        KeyCategory.Server => "\uEDA2",      // Cloud / Server
        _ => "\uE8B7"                        // Tag / Other
    };

    [JsonIgnore]
    public string MaskedSecret => string.IsNullOrEmpty(SecretValue) 
        ? string.Empty 
        : new string('•', Math.Min(SecretValue.Length, 12));
}
