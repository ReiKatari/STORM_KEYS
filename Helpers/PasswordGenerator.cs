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

    private static readonly string[] Bip39Words =
    [
        "abandon", "ability", "able", "about", "above", "absent", "absorb", "abstract",
        "absurd", "abuse", "access", "accident", "account", "accuse", "achieve", "acid",
        "acoustic", "acquire", "across", "act", "action", "actor", "actress", "actual",
        "adapt", "add", "addict", "address", "adjust", "admit", "adult", "advance",
        "advice", "aerobic", "affair", "afford", "afraid", "again", "age", "agent",
        "agree", "ahead", "aim", "air", "airport", "aisle", "alarm", "album",
        "alcohol", "alert", "alien", "all", "alley", "allow", "almost", "alone",
        "alpha", "already", "also", "alter", "always", "amateur", "amazing", "among",
        "amount", "amused", "analyst", "anchor", "ancient", "anger", "angle", "angry",
        "animal", "ankle", "announce", "annual", "another", "answer", "antenna", "antique",
        "anxiety", "any", "apart", "apology", "appear", "apple", "approve", "april",
        "arch", "arctic", "area", "arena", "argue", "arm", "armed", "armor",
        "army", "around", "arrange", "arrest", "arrive", "arrow", "art", "artefact",
        "artist", "artwork", "ask", "aspect", "assault", "asset", "assist", "assume",
        "asthma", "athlete", "atom", "attack", "attend", "attitude", "attract", "auction",
        "audit", "august", "aunt", "author", "auto", "autumn", "average", "avocado",
        "avoid", "awake", "aware", "away", "awesome", "awful", "awkward", "axis",
        "balance", "balcony", "ball", "bamboo", "banana", "banner", "bar", "barely",
        "bargain", "barrel", "base", "basic", "basket", "battle", "beach", "bean",
        "beauty", "because", "become", "beef", "before", "begin", "behave", "behind",
        "believe", "below", "belt", "bench", "benefit", "best", "betray", "better",
        "between", "beyond", "bicycle", "bid", "bike", "bind", "biology", "bird",
        "birth", "bitter", "black", "blade", "blame", "blanket", "blast", "bleak",
        "bless", "blind", "blood", "blossom", "blouse", "blue", "blur", "blush",
        "board", "boat", "body", "boil", "bomb", "bone", "bonus", "book",
        "boost", "border", "boring", "borrow", "boss", "bottom", "bounce", "box"
    ];

    public static string GenerateSeedPhrase(int wordCount = 12)
    {
        wordCount = wordCount <= 12 ? 12 : 24;
        var selected = new List<string>(wordCount);
        for (int i = 0; i < wordCount; i++)
        {
            var word = Bip39Words[RandomNumberGenerator.GetInt32(Bip39Words.Length)].ToLowerInvariant();
            selected.Add(word);
        }
        return string.Join(" ", selected);
    }

    public static string AnalyzePasswordAnatomy(string? password)
    {
        if (string.IsNullOrEmpty(password))
            return "Длина: 0 • A-Z: 0 • a-z: 0 • 0-9: 0 • Симв: 0";

        int upper = 0, lower = 0, digits = 0, symbols = 0;
        var unique = new HashSet<char>();
        foreach (char c in password)
        {
            unique.Add(c);
            if (char.IsUpper(c)) upper++;
            else if (char.IsLower(c)) lower++;
            else if (char.IsDigit(c)) digits++;
            else symbols++;
        }

        string lowerPwd = password.ToLowerInvariant();
        bool hasSequence = lowerPwd.Contains("qwerty") || lowerPwd.Contains("12345") || lowerPwd.Contains("asdf") || lowerPwd.Contains("password") || lowerPwd.Contains("admin");
        string seqNote = hasSequence ? " • ⚠️ Шаблон" : " • ✓ Без шаблонов";
        return $"Длина: {password.Length} (уник. {unique.Count}) • A-Z: {upper} • a-z: {lower} • 0-9: {digits} • Симв: {symbols}{seqNote}";
    }

    public static bool ValidateLuhnChecksum(string? cardNumber)
    {
        if (string.IsNullOrWhiteSpace(cardNumber)) return false;
        var digits = new List<int>();
        foreach (char c in cardNumber)
        {
            if (char.IsDigit(c)) digits.Add(c - '0');
            else if (c != ' ' && c != '-') return false;
        }
        if (digits.Count < 13 || digits.Count > 19) return false;

        int sum = 0;
        bool alternate = false;
        for (int i = digits.Count - 1; i >= 0; i--)
        {
            int n = digits[i];
            if (alternate)
            {
                n *= 2;
                if (n > 9) n -= 9;
            }
            sum += n;
            alternate = !alternate;
        }
        return (sum % 10) == 0;
    }

    public static string DetectCardBrand(string? cardNumber)
    {
        if (string.IsNullOrWhiteSpace(cardNumber)) return "Карта";
        var clean = new StringBuilder();
        foreach (char c in cardNumber)
        {
            if (char.IsDigit(c)) clean.Append(c);
        }
        string d = clean.ToString();
        if (d.Length == 0) return "Карта";
        if (d.StartsWith("2200") || d.StartsWith("2201") || d.StartsWith("2202") || d.StartsWith("2203") || d.StartsWith("2204")) return "МИР";
        if (d.StartsWith('4')) return "Visa";
        if (d.StartsWith("51") || d.StartsWith("52") || d.StartsWith("53") || d.StartsWith("54") || d.StartsWith("55") || d.StartsWith("222") || d.StartsWith("272")) return "MasterCard";
        if (d.StartsWith("62")) return "UnionPay";
        if (d.StartsWith("34") || d.StartsWith("37")) return "AmEx";
        if (d.StartsWith("50") || d.StartsWith("56") || d.StartsWith("57") || d.StartsWith("58") || d.StartsWith("63") || d.StartsWith("67")) return "Maestro";
        return "Карта";
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

    /// <summary>
    /// Генерирует криптографические API-токены, секреты JWT (256-bit Hex / Base64Url / Bearer / UUID v4) и ключи SSH.
    /// </summary>
    public static string GenerateApiToken(string format = "Hex256")
    {
        byte[] bytes = new byte[32]; // 256 бит энтропии
        RandomNumberGenerator.Fill(bytes);

        return (format?.Trim().ToUpperInvariant()) switch
        {
            "BASE64" or "JWT" => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_'),
            "UUID" or "GUID" => Guid.NewGuid().ToString("D"),
            "BEARER" => "sk_live_" + Convert.ToHexString(bytes).ToLowerInvariant()[..48],
            "SSH" => "ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAI" + Convert.ToBase64String(bytes).TrimEnd('=') + " storm@keys",
            _ => Convert.ToHexString(bytes).ToLowerInvariant()
        };
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
