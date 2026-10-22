using System;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Microsoft.UI.Dispatching;

namespace StormKeys.Helpers;

public static class ClipboardHelper
{
    private static string _lastCopiedText = string.Empty;
    private static DispatcherQueueTimer? _clearTimer;

    public static void CopyToClipboard(string text, DispatcherQueue dispatcherQueue, int clearAfterSeconds = 30)
    {
        if (string.IsNullOrEmpty(text))
            return;

        var dataPackage = new DataPackage();
        dataPackage.SetText(text);
        Clipboard.SetContent(dataPackage);

        _lastCopiedText = text;

        if (clearAfterSeconds > 0)
        {
            _clearTimer?.Stop();
            _clearTimer = dispatcherQueue.CreateTimer();
            _clearTimer.Interval = TimeSpan.FromSeconds(clearAfterSeconds);
            _clearTimer.IsRepeating = false;
            _clearTimer.Tick += (s, e) =>
            {
                try
                {
                    Clipboard.Clear();
                }
                catch { }
                _clearTimer?.Stop();
            };
            _clearTimer.Start();
        }
    }
}
