using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace StormKeys.Helpers;

public static class PasswordGenerator
{
    private const string UppercaseNoAmbiguous = "ABCDEFGHJKLMNPQRSTUVWXYZ";
    private const string UppercaseFull = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
    private const string LowercaseNoAmbiguous = "abcdefghijkmnpqrstuvwxyz";
    private const string LowercaseFull = "abcdefghijklmnopqrstuvwxyz";
    private const string DigitNoAmbiguous = "23456789";
    private const string DigitFull = "0123456789";
    private const string SpecialChars = "!@#$%^&*-_=+";

    private static readonly string[] PassphraseWords = new[]
    {
        "Aurora", "Quantum", "Falcon", "Crystal", "Vortex", "Nebula", "Cipher", "Cobalt",
        "Phoenix", "Obsidian", "Horizon", "Valiant", "Zenith", "Pulsar", "Titanium", "Eclipse",
        "Aegis", "Vector", "Matrix", "Prism", "Solstice", "Avalanche", "Sentinel", "Onyx",
        "Tempest", "Mirage", "Catalyst", "Paragon", "Aether", "Bastion", "Dynamo", "Glacier",
        "Monolith", "Krypton", "Chronos", "Polaris", "Spectra", "Vanguard", "Hyperion", "Synergy",
        "Citadel", "Odyssey", "Apex", "Raptor", "Helios", "Stardust", "Trident", "Quasar",
        "Borealis", "Ember", "Nova", "Stratos", "Keystone", "Arcane", "Vertex", "Radiant",
        "Pinnacle", "Fortress", "Nexus", "Equinox", "Mercury", "Astral", "Infinity", "Sapphire"
    };

    public static string Generate(
        int length = 20,
        bool useUpper = true,
        bool useLower = true,
        bool useDigits = true,
        bool useSpecial = true,
        bool excludeAmbiguous = true)
    {
        if (length < 4) length = 4;
        if (length > 128) length = 128;

        string upperPool = excludeAmbiguous ? UppercaseNoAmbiguous : UppercaseFull;
        string lowerPool = excludeAmbiguous ? LowercaseNoAmbiguous : LowercaseFull;
        string digitPool = excludeAmbiguous ? DigitNoAmbiguous : DigitFull;

        var charPool = new StringBuilder();
        var requiredChars = new List<char>();

        if (useUpper)
        {
            charPool.Append(upperPool);
            requiredChars.Add(upperPool[RandomNumberGenerator.GetInt32(upperPool.Length)]);
        }
        if (useLower)
        {
            charPool.Append(lowerPool);
            requiredChars.Add(lowerPool[RandomNumberGenerator.GetInt32(lowerPool.Length)]);
        }
        if (useDigits)
        {
            charPool.Append(digitPool);
            requiredChars.Add(digitPool[RandomNumberGenerator.GetInt32(digitPool.Length)]);
        }
        if (useSpecial)
        {
            charPool.Append(SpecialChars);
            requiredChars.Add(SpecialChars[RandomNumberGenerator.GetInt32(SpecialChars.Length)]);
        }

        if (charPool.Length == 0)
        {
            charPool.Append(lowerPool + digitPool);
            requiredChars.Add(lowerPool[RandomNumberGenerator.GetInt32(lowerPool.Length)]);
            requiredChars.Add(digitPool[RandomNumberGenerator.GetInt32(digitPool.Length)]);
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

    public static string GeneratePin(int length = 6)
    {
        if (length < 4) length = 4;
        if (length > 16) length = 16;

        var sb = new StringBuilder(length);
        for (int i = 0; i < length; i++)
        {
            sb.Append(DigitFull[RandomNumberGenerator.GetInt32(DigitFull.Length)]);
        }
        return sb.ToString();
    }

    public static string GeneratePassphrase(int wordCount = 4, string separator = "-", bool includeDigitsAndSymbol = true)
    {
        if (wordCount < 2) wordCount = 2;
        var words = new List<string>(wordCount);
        for (int i = 0; i < wordCount; i++)
        {
            int idx = RandomNumberGenerator.GetInt32(PassphraseWords.Length);
            words.Add(PassphraseWords[idx]);
        }

        string phrase = string.Join(separator, words);
        if (includeDigitsAndSymbol)
        {
            int num = RandomNumberGenerator.GetInt32(10, 99);
            char sym = SpecialChars[RandomNumberGenerator.GetInt32(SpecialChars.Length)];
            phrase = $"{phrase}{separator}{num}{sym}";
        }
        return phrase;
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

    public static double CalculateEntropyBits(string password)
    {
        if (string.IsNullOrEmpty(password))
            return 0.0;

        bool hasUpper = false, hasLower = false, hasDigit = false, hasSpecial = false;
        foreach (char c in password)
        {
            if (char.IsUpper(c)) hasUpper = true;
            else if (char.IsLower(c)) hasLower = true;
            else if (char.IsDigit(c)) hasDigit = true;
            else hasSpecial = true;
        }

        int poolSize = 0;
        if (hasUpper) poolSize += 26;
        if (hasLower) poolSize += 26;
        if (hasDigit) poolSize += 10;
        if (hasSpecial) poolSize += 32;
        if (poolSize == 0) poolSize = 26;

        double bits = password.Length * Math.Log2(poolSize);
        return Math.Round(bits, 1);
    }

    public static string EstimateCrackTime(string password)
    {
        double bits = CalculateEntropyBits(password);
        if (bits <= 0) return "—";
        if (bits < 28) return "Мгновенно (< 1 сек)";
        if (bits < 38) return "Несколько секунд";
        if (bits < 48) return "Около 15 минут";
        if (bits < 58) return "Несколько дней";
        if (bits < 68) return "Около 5 лет";
        if (bits < 80) return "> 1 000 лет";
        if (bits < 100) return "> 1 млн лет";
        return "> 1 млрд лет (Неуязвим)";
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
