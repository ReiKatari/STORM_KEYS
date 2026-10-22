using System;
using System.Security.Cryptography;
using System.Text;

namespace StormKeys.Services;

public interface ITotpService
{
    string GenerateCode(string base32Secret, DateTimeOffset? timestamp = null);
    int GetSecondsRemaining();
    double GetRemainingRatio();
    bool ValidateSecret(string base32Secret);
    string GenerateNewSecret(int byteLength = 20);
    string GenerateSecretKey(int byteLength = 20);
    string FormatCode(string code);
    string GenerateOtpAuthUri(string secret, string accountName, string issuer = "STORM KEYS");
}

public sealed class TotpService : ITotpService
{
    private static readonly char[] Base32Chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567".ToCharArray();

    public string GenerateCode(string base32Secret, DateTimeOffset? timestamp = null)
    {
        if (string.IsNullOrWhiteSpace(base32Secret))
            return string.Empty;

        try
        {
            byte[] key = Base32Decode(base32Secret);
            if (key.Length == 0)
                return string.Empty;

            long timeStep = 30;
            long unixSeconds = (timestamp ?? DateTimeOffset.UtcNow).ToUnixTimeSeconds();
            long counter = unixSeconds / timeStep;

            byte[] counterBytes = BitConverter.GetBytes(counter);
            if (BitConverter.IsLittleEndian)
                Array.Reverse(counterBytes);

            using var hmac = new HMACSHA1(key);
            byte[] hash = hmac.ComputeHash(counterBytes);

            // Dynamic truncation (RFC 4226)
            int offset = hash[^1] & 0x0F;
            int binaryCode = ((hash[offset] & 0x7F) << 24)
                           | ((hash[offset + 1] & 0xFF) << 16)
                           | ((hash[offset + 2] & 0xFF) << 8)
                           | (hash[offset + 3] & 0xFF);

            int otp = binaryCode % 1_000_000;
            return otp.ToString("D6");
        }
        catch
        {
            return string.Empty;
        }
    }

    public int GetSecondsRemaining()
    {
        long unix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        int rem = (int)(30 - (unix % 30));
        return rem <= 0 ? 30 : rem;
    }

    public double GetRemainingRatio()
    {
        return (double)GetSecondsRemaining() / 30.0;
    }

    public bool ValidateSecret(string base32Secret)
    {
        if (string.IsNullOrWhiteSpace(base32Secret))
            return false;

        string clean = base32Secret.Trim().Replace(" ", "").ToUpperInvariant();
        foreach (char c in clean)
        {
            if (Array.IndexOf(Base32Chars, c) < 0 && c != '=')
                return false;
        }

        return clean.Length >= 8;
    }

    public string GenerateNewSecret(int byteLength = 20)
    {
        byte[] bytes = new byte[byteLength];
        RandomNumberGenerator.Fill(bytes);
        return Base32Encode(bytes);
    }

    public string GenerateSecretKey(int byteLength = 20) => GenerateNewSecret(byteLength);

    public string FormatCode(string code) => string.IsNullOrEmpty(code) 
        ? string.Empty 
        : (code.Length == 6 ? $"{code[..3]} {code[3..]}" : code);

    public string GenerateOtpAuthUri(string secret, string accountName, string issuer = "STORM KEYS")
    {
        string encodedIssuer = Uri.EscapeDataString(issuer);
        string encodedAccount = Uri.EscapeDataString(accountName);
        string cleanSecret = secret.Trim().Replace(" ", "").ToUpperInvariant();

        return $"otpauth://totp/{encodedIssuer}:{encodedAccount}?secret={cleanSecret}&issuer={encodedIssuer}&algorithm=SHA1&digits=6&period=30";
    }

    private static byte[] Base32Decode(string input)
    {
        string clean = input.Trim().Replace(" ", "").Replace("-", "").TrimEnd('=').ToUpperInvariant();
        if (clean.Length == 0)
            return Array.Empty<byte>();

        int byteCount = clean.Length * 5 / 8;
        byte[] result = new byte[byteCount];

        byte curByte = 0;
        int bitsRemaining = 8;
        int mask = 0;
        int arrayIndex = 0;

        foreach (char c in clean)
        {
            int val = Array.IndexOf(Base32Chars, c);
            if (val < 0)
                continue;

            if (bitsRemaining > 5)
            {
                mask = val << (bitsRemaining - 5);
                curByte = (byte)(curByte | mask);
                bitsRemaining -= 5;
            }
            else
            {
                mask = val >> (5 - bitsRemaining);
                curByte = (byte)(curByte | mask);
                if (arrayIndex < result.Length)
                    result[arrayIndex++] = curByte;

                curByte = (byte)(val << (3 + bitsRemaining));
                bitsRemaining += 3;
            }
        }

        if (arrayIndex < result.Length)
            result[arrayIndex] = curByte;

        return result;
    }

    private static string Base32Encode(byte[] data)
    {
        if (data.Length == 0)
            return string.Empty;

        StringBuilder sb = new StringBuilder((data.Length * 8 + 4) / 5);
        int nextChar = 0;
        int bitsRemaining = 5;

        foreach (byte b in data)
        {
            nextChar = (nextChar << 8) | b;
            bitsRemaining += 8;
            while (bitsRemaining >= 5)
            {
                bitsRemaining -= 5;
                int index = (nextChar >> bitsRemaining) & 0x1F;
                sb.Append(Base32Chars[index]);
            }
        }

        if (bitsRemaining > 0)
        {
            int index = (nextChar << (5 - bitsRemaining)) & 0x1F;
            sb.Append(Base32Chars[index]);
        }

        return sb.ToString();
    }
}
