using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace StormKeys.Helpers;

/// <summary>
/// Нативный сервис быстрой вставки и авто-ввода (Auto-Type) учётных данных, ключей и 2FA-кодов
/// напрямую в активное окно браузера или программы через Win32 SendInput (KEYEVENTF_UNICODE).
/// </summary>
public static class AutoTypeHelper
{
    private const int INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const uint KEYEVENTF_UNICODE = 0x0004;

    private const ushort VK_TAB = 0x09;
    private const ushort VK_RETURN = 0x0D;
    private const ushort VK_MENU = 0x12; // Alt
    private const ushort VK_ESCAPE = 0x1B;

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public int type;
        public INPUTUNION U;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct INPUTUNION
    {
        [FieldOffset(0)]
        public MOUSEINPUT mi;
        [FieldOffset(0)]
        public KEYBDINPUT ki;
        [FieldOffset(0)]
        public HARDWAREINPUT hi;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HARDWAREINPUT
    {
        public uint uMsg;
        public ushort wParamL;
        public ushort wParamH;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    private const uint GW_HWNDNEXT = 2;
    private const int SW_MINIMIZE = 6;
    private const int SW_RESTORE = 9;

    /// <summary>
    /// Переключает фокус на предыдущее окно пользователя и печатает Логин -> Tab -> Пароль (-> Enter).
    /// </summary>
    public static async Task<bool> AutoTypeCredentialsAsync(
        IntPtr currentAppHwnd,
        string? username,
        string? secret,
        bool pressEnter = false,
        int delayBeforeTypingMs = 420)
    {
        try
        {
            if (currentAppHwnd != IntPtr.Zero)
            {
                ShowWindow(currentAppHwnd, SW_MINIMIZE);
            }

            await Task.Delay(delayBeforeTypingMs).ConfigureAwait(false);

            bool hasUser = !string.IsNullOrEmpty(username);
            bool hasSecret = !string.IsNullOrEmpty(secret);

            if (hasUser && hasSecret)
            {
                SendUnicodeText(username!);
                await Task.Delay(50).ConfigureAwait(false);
                SendVirtualKey(VK_TAB);
                await Task.Delay(60).ConfigureAwait(false);
                SendUnicodeText(secret!);
            }
            else if (hasSecret)
            {
                SendUnicodeText(secret!);
            }
            else if (hasUser)
            {
                SendUnicodeText(username!);
            }
            else
            {
                return false;
            }

            if (pressEnter)
            {
                await Task.Delay(50).ConfigureAwait(false);
                SendVirtualKey(VK_RETURN);
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Переключает фокус на предыдущее окно и вводит произвольный текст (например, 6-значный код 2FA или ключ).
    /// </summary>
    public static async Task<bool> AutoTypeTextAsync(IntPtr currentAppHwnd, string text, bool pressEnter = false, int delayBeforeTypingMs = 420)
    {
        if (string.IsNullOrEmpty(text))
            return false;

        try
        {
            if (currentAppHwnd != IntPtr.Zero)
            {
                ShowWindow(currentAppHwnd, SW_MINIMIZE);
            }

            await Task.Delay(delayBeforeTypingMs).ConfigureAwait(false);
            SendUnicodeText(text);

            if (pressEnter)
            {
                await Task.Delay(50).ConfigureAwait(false);
                SendVirtualKey(VK_RETURN);
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    public static void SendUnicodeText(string text)
    {
        if (string.IsNullOrEmpty(text))
            return;

        var inputs = new List<INPUT>(text.Length * 2);
        foreach (char c in text)
        {
            inputs.Add(new INPUT
            {
                type = INPUT_KEYBOARD,
                U = new INPUTUNION
                {
                    ki = new KEYBDINPUT
                    {
                        wVk = 0,
                        wScan = c,
                        dwFlags = KEYEVENTF_UNICODE,
                        time = 0,
                        dwExtraInfo = IntPtr.Zero
                    }
                }
            });

            inputs.Add(new INPUT
            {
                type = INPUT_KEYBOARD,
                U = new INPUTUNION
                {
                    ki = new KEYBDINPUT
                    {
                        wVk = 0,
                        wScan = c,
                        dwFlags = KEYEVENTF_UNICODE | KEYEVENTF_KEYUP,
                        time = 0,
                        dwExtraInfo = IntPtr.Zero
                    }
                }
            });
        }

        var arr = inputs.ToArray();
        SendInput((uint)arr.Length, arr, Marshal.SizeOf<INPUT>());
    }

    public static void SendVirtualKey(ushort vk)
    {
        var arr = new INPUT[2];
        arr[0] = new INPUT
        {
            type = INPUT_KEYBOARD,
            U = new INPUTUNION
            {
                ki = new KEYBDINPUT
                {
                    wVk = vk,
                    wScan = 0,
                    dwFlags = 0,
                    time = 0,
                    dwExtraInfo = IntPtr.Zero
                }
            }
        };
        arr[1] = new INPUT
        {
            type = INPUT_KEYBOARD,
            U = new INPUTUNION
            {
                ki = new KEYBDINPUT
                {
                    wVk = vk,
                    wScan = 0,
                    dwFlags = KEYEVENTF_KEYUP,
                    time = 0,
                    dwExtraInfo = IntPtr.Zero
                }
            }
        };

        SendInput((uint)arr.Length, arr, Marshal.SizeOf<INPUT>());
    }
}
