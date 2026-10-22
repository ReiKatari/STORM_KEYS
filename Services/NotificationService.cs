using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using StormKeys.Models;
#if WINDOWS
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
#endif

namespace StormKeys.Services;

public interface INotificationService
{
    event Action<string, string, KeyEntry>? OnInAppNotification;
    void StartPeriodicCheck(Func<IEnumerable<KeyEntry>> getEntriesFunc, DispatcherQueue dispatcherQueue);
    void StopPeriodicCheck();
    Task CheckExpirationsAsync(IEnumerable<KeyEntry> entries);
    void ShowToast(string title, string message, string? tag = null);
}

public class NotificationService : INotificationService
{
    private DispatcherQueueTimer? _timer;
    private Func<IEnumerable<KeyEntry>>? _getEntriesFunc;
    private bool _isInitialized;

    public event Action<string, string, KeyEntry>? OnInAppNotification;

    public NotificationService()
    {
        InitializeNotifications();
    }

    private void InitializeNotifications()
    {
        try
        {
#if WINDOWS
            if (AppNotificationManager.IsSupported())
            {
                AppNotificationManager.Default.Register();
                _isInitialized = true;
            }
#endif
        }
        catch
        {
            _isInitialized = false;
        }
    }

    public void StartPeriodicCheck(Func<IEnumerable<KeyEntry>> getEntriesFunc, DispatcherQueue dispatcherQueue)
    {
        _getEntriesFunc = getEntriesFunc;
        _timer?.Stop();

        _timer = dispatcherQueue.CreateTimer();
        _timer.Interval = TimeSpan.FromHours(1); // Проверка каждый час
        _timer.IsRepeating = true;
        _timer.Tick += async (s, e) =>
        {
            if (_getEntriesFunc != null)
            {
                var entries = _getEntriesFunc();
                await CheckExpirationsAsync(entries);
            }
        };

        _timer.Start();

        // Первичная немедленная проверка
        _ = Task.Run(async () =>
        {
            await Task.Delay(2000); // 2 сек после старта
            dispatcherQueue.TryEnqueue(async () =>
            {
                if (_getEntriesFunc != null)
                {
                    var entries = _getEntriesFunc();
                    await CheckExpirationsAsync(entries);
                }
            });
        });
    }

    public void StopPeriodicCheck()
    {
        _timer?.Stop();
        _timer = null;
    }

    public Task CheckExpirationsAsync(IEnumerable<KeyEntry> entries)
    {
        var now = DateTimeOffset.Now;

        foreach (var entry in entries)
        {
            if (!entry.ExpirationDate.HasValue)
                continue;

            int days = entry.DaysRemaining.GetValueOrDefault();

            // 1. Истёк сегодня или ранее
            if (days < 0 && !entry.NotifiedExpired)
            {
                entry.NotifiedExpired = true;
                string title = $"Срок действия истёк: {entry.Title}";
                string msg = $"Ключ/лицензия «{entry.Title}» истёк {Math.Abs(days)} дн. назад. Требуется продление!";
                ShowToast(title, msg, entry.Id.ToString());
                OnInAppNotification?.Invoke(title, msg, entry);
            }
            // 2. Истекает через 1 день (или сегодня)
            else if (days <= 1 && days >= 0 && !entry.Notified1Day)
            {
                entry.Notified1Day = true;
                string title = $"Срочно: срок действия истекает! ({entry.Title})";
                string msg = days == 0 
                    ? $"Внимание! Срок действия «{entry.Title}» истекает СЕГОДНЯ!" 
                    : $"Внимание! Срок действия «{entry.Title}» истекает ЗАВТРА ({entry.ExpirationDate.Value:dd.MM.yyyy})!";
                ShowToast(title, msg, entry.Id.ToString());
                OnInAppNotification?.Invoke(title, msg, entry);
            }
            // 3. Истекает через 7 дней
            else if (days <= 7 && days > 1 && !entry.Notified7Days)
            {
                entry.Notified7Days = true;
                string title = $"Внимание: скорое окончание ({entry.Title})";
                string msg = $"Срок действия «{entry.Title}» истекает через {days} дн. ({entry.ExpirationDate.Value:dd.MM.yyyy}).";
                ShowToast(title, msg, entry.Id.ToString());
                OnInAppNotification?.Invoke(title, msg, entry);
            }
            // 4. Истекает через 30 дней
            else if (days <= 30 && days > 7 && !entry.Notified30Days)
            {
                entry.Notified30Days = true;
                string title = $"Напоминание о продлении: {entry.Title}";
                string msg = $"Лицензия/ключ «{entry.Title}» действует еще {days} дн. (до {entry.ExpirationDate.Value:dd.MM.yyyy}).";
                ShowToast(title, msg, entry.Id.ToString());
                OnInAppNotification?.Invoke(title, msg, entry);
            }
        }

        return Task.CompletedTask;
    }

    public void ShowToast(string title, string message, string? tag = null)
    {
        try
        {
#if WINDOWS
            if (_isInitialized && AppNotificationManager.IsSupported())
            {
                var notification = new AppNotificationBuilder()
                    .AddText(title)
                    .AddText(message)
                    .SetAttributionText("STORM KEYS")
                    .BuildNotification();

                if (!string.IsNullOrEmpty(tag))
                    notification.Tag = tag;

                AppNotificationManager.Default.Show(notification);
                return;
            }
#endif
        }
        catch
        {
            // Fallback без краша
        }
    }
}
