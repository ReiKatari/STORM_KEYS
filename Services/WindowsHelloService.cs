using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Windows.Security.Credentials;

namespace StormKeys.Services;

public interface IWindowsHelloService
{
    Task<bool> IsSupportedAsync();
    bool IsEnrolled();
    Task<bool> EnrollAsync(string masterPassword);
    Task<string?> UnlockWithHelloAsync();
    Task DisableAsync();
}

public sealed class WindowsHelloService : IWindowsHelloService
{
    private const string CredentialAccountName = "STORM_KEYS_BIOMETRIC_VAULT";
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("STORM_KEYS_HELLO_ENTROPY_2026");

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

    public async Task<bool> EnrollAsync(string masterPassword)
    {
        if (string.IsNullOrEmpty(masterPassword))
            return false;

        try
        {
            if (!await IsSupportedAsync())
                return false;

            var result = await KeyCredentialManager.RequestCreateAsync(
                CredentialAccountName,
                KeyCredentialCreationOption.ReplaceExisting);

            if (result.Status == KeyCredentialStatus.Success)
            {
                byte[] raw = Encoding.UTF8.GetBytes(masterPassword);
                byte[] encrypted = ProtectBytes(raw, Entropy);
                await File.WriteAllBytesAsync(GetStoragePath(), encrypted);
                return true;
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    public async Task<string?> UnlockWithHelloAsync()
    {
        string path = GetStoragePath();
        if (!File.Exists(path))
            return null;

        try
        {
            if (!await IsSupportedAsync())
                return null;

            var result = await KeyCredentialManager.OpenAsync(CredentialAccountName);
            if (result.Status == KeyCredentialStatus.Success && result.Credential != null)
            {
                var challenge = Windows.Security.Cryptography.CryptographicBuffer.ConvertStringToBinary(
                    "STORM_KEYS_AUTH_" + DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                    Windows.Security.Cryptography.BinaryStringEncoding.Utf8);
                var signResult = await result.Credential.RequestSignAsync(challenge);
                if (signResult.Status == KeyCredentialStatus.Success)
                {
                    byte[] encrypted = await File.ReadAllBytesAsync(path);
                    byte[] decrypted = UnprotectBytes(encrypted, Entropy);
                    return Encoding.UTF8.GetString(decrypted);
                }
            }

            return null;
        }
        catch
        {
            return null;
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
