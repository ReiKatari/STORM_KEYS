using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Konscious.Security.Cryptography;
using StormKeys.Models;

namespace StormKeys.Services;

public interface ICryptoService
{
    Task<byte[]> EncryptVaultAsync(VaultContainer container, string masterPassword);
    Task<VaultContainer> DecryptVaultAsync(byte[] encryptedData, string masterPassword);
    Task<bool> VerifyMasterPasswordAsync(byte[] encryptedData, string masterPassword);
    Task<string> GenerateKeyfileAsync(string keyfilePath);
    string CombinePasswordWithKeyfile(string masterPassword, string? keyfilePath);
    string ComputePasswordVerifierHash(string password);
}

public class CryptoService : ICryptoService
{
    private static readonly byte[] MagicBytes = Encoding.ASCII.GetBytes("STORMKEY");
    private const byte CurrentFormatVersion = 0x01;
    private const int SaltSize = 16;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int KeySize = 32; // 256 bits for AES-256-GCM

    // Argon2id параметры безопасности
    private const int Argon2MemoryCost = 65536; // 64 MB
    private const int Argon2Iterations = 4;
    private static readonly int Argon2Parallelism = Math.Max(1, Math.Min(4, Environment.ProcessorCount));

    /// <summary>
    /// Создаёт криптографический 64-байтный ключевой файл (.stormkey) для двухфакторной защиты контейнера.
    /// </summary>
    public async Task<string> GenerateKeyfileAsync(string keyfilePath)
    {
        byte[] rawKey = new byte[64];
        RandomNumberGenerator.Fill(rawKey);
        string content = "STORM-KEYFILE-V1:" + Convert.ToBase64String(rawKey);
        string? dir = Path.GetDirectoryName(keyfilePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        await File.WriteAllTextAsync(keyfilePath, content, Encoding.UTF8);
        CryptographicOperations.ZeroMemory(rawKey);
        return keyfilePath;
    }

    /// <summary>
    /// Комбинирует мастер-пароль и содержимое ключевого файла (.stormkey) через HMAC-SHA256.
    /// Если ключевой файл не задан, возвращает исходный мастер-пароль.
    /// </summary>
    public string CombinePasswordWithKeyfile(string masterPassword, string? keyfilePath)
    {
        if (string.IsNullOrWhiteSpace(keyfilePath) || !File.Exists(keyfilePath))
            return masterPassword;

        byte[] fileBytes = File.ReadAllBytes(keyfilePath);
        byte[] passBytes = Encoding.UTF8.GetBytes(masterPassword);
        using var hmac = new HMACSHA256(fileBytes);
        byte[] mac = hmac.ComputeHash(passBytes);
        return $"{masterPassword}::KF::{Convert.ToHexString(mac)}";
    }

    /// <summary>
    /// Вычисляет криптостойкий SHA-256 хеш с солью для проверки пароля под принуждением (Duress Password).
    /// </summary>
    public string ComputePasswordVerifierHash(string password)
    {
        if (string.IsNullOrEmpty(password))
            return string.Empty;

        byte[] bytes = Encoding.UTF8.GetBytes("STORM_DURESS_SALT_V1::" + password);
        byte[] hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash);
    }

    public async Task<byte[]> EncryptVaultAsync(VaultContainer container, string masterPassword)
    {
        if (string.IsNullOrEmpty(masterPassword))
            throw new ArgumentException("Мастер-пароль не может быть пустым.", nameof(masterPassword));

        container.Metadata.LastModifiedAt = DateTimeOffset.UtcNow;
        container.Metadata.EntryCount = container.Entries.Count;

        string json = JsonSerializer.Serialize(container, new JsonSerializerOptions
        {
            WriteIndented = false
        });

        byte[] plaintextBytes = Encoding.UTF8.GetBytes(json);
        byte[] salt = new byte[SaltSize];
        byte[] nonce = new byte[NonceSize];
        byte[] tag = new byte[TagSize];
        byte[] ciphertext = new byte[plaintextBytes.Length];
        byte[] derivedKey = Array.Empty<byte>();

        try
        {
            RandomNumberGenerator.Fill(salt);
            RandomNumberGenerator.Fill(nonce);

            derivedKey = await DeriveKeyAsync(masterPassword, salt);

            using (var aesGcm = new AesGcm(derivedKey, TagSize))
            {
                aesGcm.Encrypt(nonce, plaintextBytes, ciphertext, tag);
            }

            using var memoryStream = new MemoryStream();
            using (var writer = new BinaryWriter(memoryStream, Encoding.UTF8, leaveOpen: true))
            {
                writer.Write(MagicBytes);
                writer.Write(CurrentFormatVersion);
                writer.Write(salt);
                writer.Write(nonce);
                writer.Write(tag);
                writer.Write(ciphertext.Length);
                writer.Write(ciphertext);
            }

            return memoryStream.ToArray();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintextBytes);
            if (derivedKey.Length > 0)
                CryptographicOperations.ZeroMemory(derivedKey);
        }
    }

    public async Task<VaultContainer> DecryptVaultAsync(byte[] encryptedData, string masterPassword)
    {
        if (string.IsNullOrEmpty(masterPassword))
            throw new ArgumentException("Мастер-пароль не может быть пустым.", nameof(masterPassword));

        if (encryptedData == null || encryptedData.Length < (MagicBytes.Length + 1 + SaltSize + NonceSize + TagSize + 4))
            throw new InvalidDataException("Файл хранилища поврежден или имеет неверный формат.");

        using var memoryStream = new MemoryStream(encryptedData);
        using var reader = new BinaryReader(memoryStream, Encoding.UTF8);

        byte[] magic = reader.ReadBytes(MagicBytes.Length);
        for (int i = 0; i < MagicBytes.Length; i++)
        {
            if (magic[i] != MagicBytes[i])
                throw new InvalidDataException("Неверная сигнатура хранилища STORM KEYS.");
        }

        byte version = reader.ReadByte();
        if (version != CurrentFormatVersion)
            throw new InvalidDataException($"Неподдерживаемая версия формата: {version}");

        byte[] salt = reader.ReadBytes(SaltSize);
        byte[] nonce = reader.ReadBytes(NonceSize);
        byte[] tag = reader.ReadBytes(TagSize);
        int cipherLength = reader.ReadInt32();

        if (cipherLength < 0 || cipherLength > 100 * 1024 * 1024)
            throw new InvalidDataException("Недопустимый размер данных хранилища.");

        byte[] ciphertext = reader.ReadBytes(cipherLength);
        if (ciphertext.Length != cipherLength)
            throw new EndOfStreamException("Неожиданный конец потока при чтении хранилища.");

        byte[] plaintextBytes = new byte[cipherLength];
        byte[] derivedKey = Array.Empty<byte>();

        try
        {
            derivedKey = await DeriveKeyAsync(masterPassword, salt);

            using (var aesGcm = new AesGcm(derivedKey, TagSize))
            {
                // AesGcm.Decrypt выбросит CryptographicException, если мастер-пароль неверен (tag mismatch)
                aesGcm.Decrypt(nonce, ciphertext, tag, plaintextBytes);
            }

            string json = Encoding.UTF8.GetString(plaintextBytes);
            var container = JsonSerializer.Deserialize<VaultContainer>(json);
            return container ?? throw new InvalidDataException("Не удалось восстановить данные хранилища.");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintextBytes);
            if (derivedKey.Length > 0)
                CryptographicOperations.ZeroMemory(derivedKey);
        }
    }

    public async Task<bool> VerifyMasterPasswordAsync(byte[] encryptedData, string masterPassword)
    {
        try
        {
            await DecryptVaultAsync(encryptedData, masterPassword);
            return true;
        }
        catch (CryptographicException)
        {
            return false;
        }
        catch
        {
            return false;
        }
    }

    private static async Task<byte[]> DeriveKeyAsync(string masterPassword, byte[] salt)
    {
        return await Task.Run(() =>
        {
            byte[] passwordBytes = Encoding.UTF8.GetBytes(masterPassword);
            try
            {
                using var argon2 = new Argon2id(passwordBytes)
                {
                    Salt = salt,
                    DegreeOfParallelism = Argon2Parallelism,
                    MemorySize = Argon2MemoryCost,
                    Iterations = Argon2Iterations
                };

                return argon2.GetBytes(KeySize);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(passwordBytes);
            }
        });
    }
}
