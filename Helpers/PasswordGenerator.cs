using System;
using System.Security.Cryptography;
using System.Text;

namespace StormKeys.Helpers;

public static class PasswordGenerator
{
    private const string UppercaseChars = "ABCDEFGHJKLMNPQRSTUVWXYZ";
    private const string LowercaseChars = "abcdefghijkmnpqrstuvwxyz";
    private const string DigitChars = "23456789";
    private const string SpecialChars = "!@#$%^&*-_=+";

    public static string Generate(int length = 20, bool useUpper = true, bool useLower = true, bool useDigits = true, bool useSpecial = true)
    {
        var charPool = new StringBuilder();
        if (useUpper) charPool.Append(UppercaseChars);
        if (useLower) charPool.Append(LowercaseChars);
        if (useDigits) charPool.Append(DigitChars);
        if (useSpecial) charPool.Append(SpecialChars);

        if (charPool.Length == 0)
            charPool.Append(LowercaseChars + DigitChars);

        string pool = charPool.ToString();
        var result = new StringBuilder(length);

        for (int i = 0; i < length; i++)
        {
            int index = RandomNumberGenerator.GetInt32(pool.Length);
            result.Append(pool[index]);
        }

        return result.ToString();
    }

    public static string GenerateLicenseKey(int groups = 5, int groupLength = 5)
    {
        const string pool = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        var sb = new StringBuilder();

        for (int g = 0; g < groups; g++)
        {
            if (g > 0) sb.Append('-');
            for (int i = 0; i < groupLength; i++)
            {
                int index = RandomNumberGenerator.GetInt32(pool.Length);
                sb.Append(pool[index]);
            }
        }

        return sb.ToString();
    }

    public static (int Score, string Label, string ColorHex) EvaluateStrength(string password)
    {
        if (string.IsNullOrEmpty(password))
            return (0, "Пусто", "#7A7A7A");

        int score = 0;
        if (password.Length >= 8) score++;
        if (password.Length >= 12) score++;
        if (password.Length >= 16) score++;

        bool hasUpper = false, hasLower = false, hasDigit = false, hasSpecial = false;
        foreach (char c in password)
        {
            if (char.IsUpper(c)) hasUpper = true;
            else if (char.IsLower(c)) hasLower = true;
            else if (char.IsDigit(c)) hasDigit = true;
            else hasSpecial = true;
        }

        if (hasUpper && hasLower) score++;
        if (hasDigit) score++;
        if (hasSpecial) score++;

        return score switch
        {
            <= 2 => (1, "Слабый", "#D13438"),
            <= 4 => (2, "Средний", "#FF8C00"),
            5 => (3, "Надежный", "#107C41"),
            _ => (4, "Максимальный", "#00B7C3")
        };
    }
}
