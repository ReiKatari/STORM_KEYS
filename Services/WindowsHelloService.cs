using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Security.Credentials;
using Windows.Security.Credentials.UI;
using WinRT;

namespace StormKeys.Services;

public interface IWindowsHelloService
{
    Task<bool> IsSupportedAsync();
    bool IsEnrolled();
    Task<bool> EnrollAsync(string masterPassword, IntPtr ownerHwnd = default);
    Task<bool> SaveEncryptedCredentialsSilentAsync(string masterPassword);
    Task<string?> UnlockWithHelloAsync(IntPtr ownerHwnd = default);
    Task DisableAsync();
}

public sealed class WindowsHelloService : IWindowsHelloService
{
    private const string CredentialAccountName = "STORM_KEYS_BIOMETRIC_VAULT";
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("STORM_KEYS_HELLO_ENTROPY_2026");

    [ComImport]
    [Guid("39E050C3-4E74-441A-8DC0-B81104DF949C")]
    [InterfaceType(ComInterfaceType.InterfaceIsIInspectable)]
    private interface IUserConsentVerifierInterop
    {
        IntPtr RequestVerificationForWindowAsync(
            IntPtr appWindow,
            [MarshalAs(UnmanagedType.HString)] string message,
            [In] ref Guid riid);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DATA_BLOB
    {
        public int cbData;
        public IntPtr pbData;
    }

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern bool CryptProtectData(
        ref DATA_BLOB pDataIn,
        string szDataDescr,
        ref DATA_BLOB pOptionalEntropy,
        IntPtr pvReserved,
        IntPtr pPromptStruct,
        int dwFlags,
        ref DATA_BLOB pDataOut);

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern bool CryptUnprotectData(
        ref DATA_BLOB pDataIn,
        IntPtr ppszDataDescr,
        ref DATA_BLOB pOptionalEntropy,
        IntPtr pvReserved,
        IntPtr pPromptStruct,
        int dwFlags,
        ref DATA_BLOB pDataOut);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr LocalFree(IntPtr hMem);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindowW(string? lpClassName, string? lpWindowName);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_SHOWWINDOW = 0x0040;

    private static string GetStoragePath()
    {
        string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "STORM KEYS");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "hello.vault");
    }

    public async Task<bool> IsSupportedAsync()
    {
        try
        {
            var availability = await UserConsentVerifier.CheckAvailabilityAsync();
            if (availability == UserConsentVerifierAvailability.Available)
                return true;

            return await KeyCredentialManager.IsSupportedAsync();
        }
        catch
        {
            return false;
        }
    }

    public bool IsEnrolled()
    {
        try
        {
            return File.Exists(GetStoragePath());
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Тихо сохраняет зашифрованный через DPAPI мастер-пароль в hello.vault без повторного вызова окна PIN-кода,
    /// когда пользователь уже успешно ввёл мастер-пароль вручную.
    /// </summary>
    public async Task<bool> SaveEncryptedCredentialsSilentAsync(string masterPassword)
    {
        if (string.IsNullOrEmpty(masterPassword))
            return false;

        try
        {
            byte[] raw = Encoding.UTF8.GetBytes(masterPassword);
            byte[] encrypted = ProtectBytes(raw, Entropy);
            await File.WriteAllBytesAsync(GetStoragePath(), encrypted);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> EnrollAsync(string masterPassword, IntPtr ownerHwnd = default)
    {
        if (string.IsNullOrEmpty(masterPassword))
            return false;

        // Сохраняем DPAPI-контейнер сразу без навязчивого повторного запроса PIN при вводе пароля
        return await SaveEncryptedCredentialsSilentAsync(masterPassword);
    }

    public async Task<string?> UnlockWithHelloAsync(IntPtr ownerHwnd = default)
    {
        string path = GetStoragePath();
        if (!File.Exists(path))
            return null;

        using var cts = new CancellationTokenSource();
        try
        {
            // Запускаем фоновый помощник, который гарантированно выводит окно ввода PIN Windows Hello ПОВЕРХ программы
            _ = BringWindowsSecurityDialogToFrontAsync(cts.Token);

            bool verified = await VerifyUserConsentOnTopAsync(ownerHwnd, "Подтвердите вход в хранилище STORM KEYS");
            cts.Cancel();

            if (!verified)
                return null;

            byte[] encrypted = await File.ReadAllBytesAsync(path);
            byte[] decrypted = UnprotectBytes(encrypted, Entropy);
            return Encoding.UTF8.GetString(decrypted);
        }
        catch
        {
            cts.Cancel();
            return null;
        }
    }

    private static async Task<bool> VerifyUserConsentOnTopAsync(IntPtr ownerHwnd, string message)
    {
        // 1. Попытка через нативный IUserConsentVerifierInterop с привязкой к HWND главного окна (всегда поверх окна)
        if (ownerHwnd != IntPtr.Zero)
        {
            try
            {
                SetForegroundWindow(ownerHwnd);
                var factory = ActivationFactory.Get("Windows.Security.Credentials.UI.UserConsentVerifier");
                var interop = Marshal.GetTypedObjectForIUnknown(factory.ThisPtr, typeof(IUserConsentVerifierInterop)) as IUserConsentVerifierInterop;
                // IID_IAsyncOperation_UserConsentVerificationResult: {fd596ffd-2318-558f-9dbe-d26b83933560}
                Guid iid = new("fd596ffd-2318-558f-9dbe-d26b83933560");
                IntPtr opPtr = interop != null ? interop.RequestVerificationForWindowAsync(ownerHwnd, message, ref iid) : IntPtr.Zero;
                if (opPtr != IntPtr.Zero)
                {
                    try
                    {
                        var asyncOp = MarshalInspectable<IAsyncOperation<UserConsentVerificationResult>>.FromAbi(opPtr);
                        var res = await asyncOp;
                        return res == UserConsentVerificationResult.Verified;
                    }
                    finally
                    {
                        Marshal.Release(opPtr);
                    }
                }
            }
            catch
            {
                // Fallback ниже
            }
        }

        // 2. Стандартный вызов UserConsentVerifier (в паре с BringWindowsSecurityDialogToFrontAsync)
        try
        {
            var res = await UserConsentVerifier.RequestVerificationAsync(message);
            if (res == UserConsentVerificationResult.Verified)
                return true;
            if (res == UserConsentVerificationResult.Canceled)
                return false;
        }
        catch { }

        // 3. Резервный путь через KeyCredentialManager
        try
        {
            var openRes = await KeyCredentialManager.OpenAsync(CredentialAccountName);
            if (openRes.Status == KeyCredentialStatus.Success && openRes.Credential != null)
            {
                var challenge = Windows.Security.Cryptography.CryptographicBuffer.ConvertStringToBinary(
                    "STORM_KEYS_AUTH_" + DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                    Windows.Security.Cryptography.BinaryStringEncoding.Utf8);
                var signResult = await openRes.Credential.RequestSignAsync(challenge);
                return signResult.Status == KeyCredentialStatus.Success;
            }
            else
            {
                var createRes = await KeyCredentialManager.RequestCreateAsync(
                    CredentialAccountName,
                    KeyCredentialCreationOption.ReplaceExisting);
                return createRes.Status == KeyCredentialStatus.Success;
            }
        }
        catch
        {
            return false;
        }
    }

    private static async Task BringWindowsSecurityDialogToFrontAsync(CancellationToken token)
    {
        string[] dialogClasses = { "Credential Dialog Xaml Host", "#32770" };
        string[] dialogTitles = { "Безопасность Windows", "Windows Security" };

        for (int i = 0; i < 40 && !token.IsCancellationRequested; i++)
        {
            try
            {
                IntPtr credHwnd = IntPtr.Zero;
                foreach (var cls in dialogClasses)
                {
                    credHwnd = FindWindowW(cls, null);
                    if (credHwnd != IntPtr.Zero) break;
                }

                if (credHwnd == IntPtr.Zero)
                {
                    foreach (var title in dialogTitles)
                    {
                        credHwnd = FindWindowW(null, title);
                        if (credHwnd != IntPtr.Zero) break;
                    }
                }

                if (credHwnd != IntPtr.Zero)
                {
                    SetWindowPos(credHwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW);
                    SetForegroundWindow(credHwnd);
                    await Task.Delay(250, token).ConfigureAwait(false);
                }
            }
            catch { }

            try
            {
                await Task.Delay(80, token).ConfigureAwait(false);
            }
            catch
            {
                break;
            }
        }
    }

    public async Task DisableAsync()
    {
        try
        {
            string path = GetStoragePath();
            if (File.Exists(path))
            {
                File.Delete(path);
            }

            await KeyCredentialManager.DeleteAsync(CredentialAccountName);
        }
        catch { }
    }

    private static byte[] ProtectBytes(byte[] data, byte[] entropy)
    {
        GCHandle dataHandle = GCHandle.Alloc(data, GCHandleType.Pinned);
        GCHandle entropyHandle = GCHandle.Alloc(entropy, GCHandleType.Pinned);
        DATA_BLOB inBlob = new DATA_BLOB { cbData = data.Length, pbData = dataHandle.AddrOfPinnedObject() };
        DATA_BLOB entropyBlob = new DATA_BLOB { cbData = entropy.Length, pbData = entropyHandle.AddrOfPinnedObject() };
        DATA_BLOB outBlob = default;

        try
        {
            if (!CryptProtectData(ref inBlob, "STORM_KEYS_HELLO", ref entropyBlob, IntPtr.Zero, IntPtr.Zero, 0, ref outBlob))
            {
                throw new InvalidOperationException($"CryptProtectData failed with error {Marshal.GetLastWin32Error()}");
            }

            byte[] result = new byte[outBlob.cbData];
            Marshal.Copy(outBlob.pbData, result, 0, outBlob.cbData);
            return result;
        }
        finally
        {
            dataHandle.Free();
            entropyHandle.Free();
            if (outBlob.pbData != IntPtr.Zero)
            {
                LocalFree(outBlob.pbData);
            }
        }
    }

    private static byte[] UnprotectBytes(byte[] encryptedData, byte[] entropy)
    {
        GCHandle dataHandle = GCHandle.Alloc(encryptedData, GCHandleType.Pinned);
        GCHandle entropyHandle = GCHandle.Alloc(entropy, GCHandleType.Pinned);
        DATA_BLOB inBlob = new DATA_BLOB { cbData = encryptedData.Length, pbData = dataHandle.AddrOfPinnedObject() };
        DATA_BLOB entropyBlob = new DATA_BLOB { cbData = entropy.Length, pbData = entropyHandle.AddrOfPinnedObject() };
        DATA_BLOB outBlob = default;

        try
        {
            if (!CryptUnprotectData(ref inBlob, IntPtr.Zero, ref entropyBlob, IntPtr.Zero, IntPtr.Zero, 0, ref outBlob))
            {
                throw new InvalidOperationException($"CryptUnprotectData failed with error {Marshal.GetLastWin32Error()}");
            }

            byte[] result = new byte[outBlob.cbData];
            Marshal.Copy(outBlob.pbData, result, 0, outBlob.cbData);
            return result;
        }
        finally
        {
            dataHandle.Free();
            entropyHandle.Free();
            if (outBlob.pbData != IntPtr.Zero)
            {
                LocalFree(outBlob.pbData);
            }
        }
    }
}
