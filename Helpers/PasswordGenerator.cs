using System;
using System.Collections.Generic;
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
        var requiredChars = new List<char>();

        if (useUpper)
        {
            charPool.Append(UppercaseChars);
            requiredChars.Add(UppercaseChars[RandomNumberGenerator.GetInt32(UppercaseChars.Length)]);
        }
        if (useLower)
        {
            charPool.Append(LowercaseChars);
            requiredChars.Add(LowercaseChars[RandomNumberGenerator.GetInt32(LowercaseChars.Length)]);
        }
        if (useDigits)
        {
            charPool.Append(DigitChars);
            requiredChars.Add(DigitChars[RandomNumberGenerator.GetInt32(DigitChars.Length)]);
        }
        if (useSpecial)
        {
            charPool.Append(SpecialChars);
            requiredChars.Add(SpecialChars[RandomNumberGenerator.GetInt32(SpecialChars.Length)]);
        }

        if (charPool.Length == 0)
        {
            charPool.Append(LowercaseChars + DigitChars);
            requiredChars.Add(LowercaseChars[RandomNumberGenerator.GetInt32(LowercaseChars.Length)]);
            requiredChars.Add(DigitChars[RandomNumberGenerator.GetInt32(DigitChars.Length)]);
        }

        string pool = charPool.ToString();
        var resultList = new List<char>(requiredChars);

        while (resultList.Count < length)
        {
            int index = RandomNumberGenerator.GetInt32(pool.Length);
            resultList.Add(pool[index]);
        }

        // Cryptographic Fisher-Yates Shuffle
        for (int i = resultList.Count - 1; i > 0; i--)
        {
            int j = RandomNumberGenerator.GetInt32(i + 1);
            (resultList[i], resultList[j]) = (resultList[j], resultList[i]);
        }

        return new string(resultList.ToArray());
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
