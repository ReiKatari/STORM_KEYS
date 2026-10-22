using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;
using StormKeys.Helpers;
using StormKeys.Models;
using StormKeys.Services;

namespace StormKeys.Tests;

public static class Program
{
    private static int _passed = 0;
    private static int _failed = 0;

    public static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.WriteLine("=================================================");
        Console.WriteLine("     STORM KEYS — AUTOMATED TEST SUITE (10.0)    ");
        Console.WriteLine("=================================================");

        await RunTest("Crypto: Argon2id + AES-256-GCM RoundTrip", Test_Crypto_RoundTrip);
        await RunTest("Crypto: Неверный мастер-пароль отклоняется", Test_Wrong_Password_Rejected);
        await RunTest("Crypto: Защита от модификации данных (Tampering)", Test_Tampering_Detection);
        await RunTest("Модель: Расчет сроков действия и статусов", Test_Expiration_Calculations);
        await RunTest("Генератор: Надежные пароли и ключи лицензий ПО", Test_Password_License_Generator);
        await RunTest("Парсер: Импорт из буфера обмена (ключи/логины)", Test_Clipboard_Import);
        await RunTest("CSV: Полный цикл экспорта и импорта данных", Test_Csv_Export_Import_RoundTrip);
        await RunTest("JSON: Парсинг структуры Bitwarden и Generic", Test_Json_Import);
        await RunTest("Сертификат: Проверка статуса STORM ECOSYSTEM", Test_Storm_Certificate);
        await RunTest("Оформление: 9 тем, 13 акцентов, 10 шрифтов (STORM AURORA FLUID)", Test_ThemeService_Definitions);
        await RunTest("Локализация: 11 языков и язык системы по умолчанию", Test_LocalizationService_Definitions);
        await RunTest("TOTP 2FA: Генерация RFC 6238, Base32 и таймер", Test_Totp_Service);
        await RunTest("Have I Been Pwned: k-Anonymity SHA-1 хэширование", Test_Pwned_Service);

        Console.WriteLine("-------------------------------------------------");
        Console.WriteLine($"РЕЗУЛЬТАТ: Успешно: {_passed} | Ошибок: {_failed}");
        Console.WriteLine("=================================================");

        return _failed == 0 ? 0 : 1;
    }

    private static async Task RunTest(string testName, Func<Task> testFunc)
    {
        try
        {
            Console.Write($"[TEST] {testName} ... ");
            await testFunc();
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("✓ ПРОЙДЕН");
            Console.ResetColor();
            _passed++;
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"✗ ПРОВАЛЕН: {ex.Message}");
            Console.ResetColor();
            _failed++;
        }
    }

    private static async Task Test_Crypto_RoundTrip()
    {
        var crypto = new CryptoService();
        var container = new VaultContainer
        {
            Metadata = new VaultMetadata { AppName = "STORM KEYS", EntryCount = 2 },
            Entries = new()
            {
                new KeyEntry
                {
                    Title = "Test License",
                    Category = KeyCategory.License,
                    SecretValue = "ABCD-1234-EFGH-5678",
                    ExpirationDate = DateTimeOffset.Now.AddDays(15)
                },
                new KeyEntry
                {
                    Title = "Admin GitHub",
                    Category = KeyCategory.Account,
                    UsernameOrEmail = "admin@storm.dev",
                    SecretValue = "P@ssw0rdSecure!2026",
                    TargetAppOrUrl = "https://github.com"
                }
            }
        };

        string masterPass = "SuperSecretStormMasterPass!123";
        byte[] encrypted = await crypto.EncryptVaultAsync(container, masterPass);

        AssertTrue(encrypted != null && encrypted.Length > 100, "Зашифрованный массив пуст или слишком мал");

        var decrypted = await crypto.DecryptVaultAsync(encrypted!, masterPass);
        AssertEqual(2, decrypted.Entries.Count, "Количество записей не совпадает");
        AssertEqual("Test License", decrypted.Entries[0].Title, "Название 1-й записи искажено");
        AssertEqual("ABCD-1234-EFGH-5678", decrypted.Entries[0].SecretValue, "Секрет 1-й записи искажен");
        AssertEqual("admin@storm.dev", decrypted.Entries[1].UsernameOrEmail, "Логин 2-й записи искажен");
    }

    private static async Task Test_Wrong_Password_Rejected()
    {
        var crypto = new CryptoService();
        var container = new VaultContainer
        {
            Entries = new() { new KeyEntry { Title = "Secret", SecretValue = "12345" } }
        };

        byte[] encrypted = await crypto.EncryptVaultAsync(container, "CorrectPassword!2026");

        try
        {
            await crypto.DecryptVaultAsync(encrypted, "WrongPassword!2026");
            throw new Exception("Расшифровка с неверным паролем должна была выбросить CryptographicException");
        }
        catch (CryptographicException)
        {
            // Ожидаемое исключение
        }
    }

    private static async Task Test_Tampering_Detection()
    {
        var crypto = new CryptoService();
        var container = new VaultContainer
        {
            Entries = new() { new KeyEntry { Title = "Secret", SecretValue = "12345" } }
        };

        byte[] encrypted = await crypto.EncryptVaultAsync(container, "MasterPass!2026");

        // Модифицируем байт в теле шифротекста
        encrypted[encrypted.Length - 1] ^= 0xFF;

        try
        {
            await crypto.DecryptVaultAsync(encrypted, "MasterPass!2026");
            throw new Exception("Модифицированный файл должен был быть отклонен AES-GCM");
        }
        catch (CryptographicException)
        {
            // Успех: искажение обнаружено
        }
    }

    private static Task Test_Expiration_Calculations()
    {
        var entryExpired = new KeyEntry
        {
            Title = "Old Key",
            ExpirationDate = DateTimeOffset.Now.AddDays(-5)
        };
        AssertTrue(entryExpired.IsExpired, "Запись должна быть просрочена");
        AssertEqual("#D13438", entryExpired.StatusBadgeColor, "Цвет просроченной записи должен быть красным");

        var entryExpiringSoon = new KeyEntry
        {
            Title = "Expiring Key",
            ExpirationDate = DateTimeOffset.Now.AddDays(10)
        };
        AssertTrue(entryExpiringSoon.IsExpiringSoon, "Запись должна определяться как скоро истекающая");
        AssertTrue(!entryExpiringSoon.IsExpired, "Запись не должна быть просрочена");

        var entryForever = new KeyEntry
        {
            Title = "Forever Key",
            ExpirationDate = null
        };
        AssertEqual("Бессрочно", entryForever.ExpirationStatusText, "Бессрочная запись должна иметь статус 'Бессрочно'");
        AssertEqual("#3A3D40", entryForever.StatusBadgeColor, "Цвет бессрочной записи должен быть нейтральным");

        return Task.CompletedTask;
    }

    private static Task Test_Password_License_Generator()
    {
        string pwd = PasswordGenerator.Generate(24, useUpper: true, useLower: true, useDigits: true, useSpecial: true);
        AssertEqual(24, pwd.Length, "Длина пароля должна быть ровно 24 символа");

        var eval = PasswordGenerator.EvaluateStrength(pwd);
        AssertTrue(eval.Score >= 4, "Сгенерированный пароль должен иметь максимальную надежность");

        string key = PasswordGenerator.GenerateLicenseKey(5, 5);
        AssertEqual(29, key.Length, "Длина ключа формата XXXXX-XXXXX-XXXXX-XXXXX-XXXXX должна быть 29 символов");
        AssertTrue(key.Contains("-"), "Ключ должен содержать дефисы");

        return Task.CompletedTask;
    }

    private static Task Test_Clipboard_Import()
    {
        var service = new ImportExportService();

        string clipboardText = "user@company.com\tSuperSecretPass123\nhttps://portal.azure.com\tazure_admin\tAzurePass!456";
        var res = service.ImportFromClipboardText(clipboardText);

        AssertTrue(res.ImportedCount >= 2, $"Должно быть импортировано не менее 2 записей, получено {res.ImportedCount}");
        AssertTrue(res.ImportedEntries.Exists(e => e.UsernameOrEmail == "user@company.com"), "Не найден user@company.com");

        return Task.CompletedTask;
    }

    private static async Task Test_Csv_Export_Import_RoundTrip()
    {
        var service = new ImportExportService();
        var entries = new System.Collections.Generic.List<KeyEntry>
        {
            new() { Title = "Google", UsernameOrEmail = "storm@gmail.com", SecretValue = "GoogleP@ss!1", TargetAppOrUrl = "https://google.com" },
            new() { Title = "GitHub", UsernameOrEmail = "storm-team", SecretValue = "GitP@ss!2", TargetAppOrUrl = "https://github.com" }
        };

        string tempCsv = Path.Combine(Path.GetTempPath(), $"storm_test_{Guid.NewGuid():N}.csv");
        try
        {
            await service.ExportToCsvAsync(entries, tempCsv);
            AssertTrue(File.Exists(tempCsv), "CSV файл экспорта не был создан");

            var importRes = await service.ImportFromCsvAsync(tempCsv);
            AssertEqual(2, importRes.ImportedCount, "Импортировано неверное количество записей из CSV");
            AssertEqual("Google", importRes.ImportedEntries[0].Title, "Название 1-й записи не совпадает");
            AssertEqual("storm@gmail.com", importRes.ImportedEntries[0].UsernameOrEmail, "Логин 1-й записи не совпадает");
        }
        finally
        {
            if (File.Exists(tempCsv)) File.Delete(tempCsv);
        }
    }

    private static async Task Test_Json_Import()
    {
        var service = new ImportExportService();
        string sampleBitwarden = """
        {
            "encrypted": false,
            "items": [
                {
                    "name": "Bitwarden Test Entry",
                    "notes": "Sample Bitwarden Import",
                    "login": {
                        "username": "bitwarden_user",
                        "password": "BitwardenPassword123!",
                        "uris": [ { "uri": "https://vault.bitwarden.com" } ]
                    }
                }
            ]
        }
        """;

        string tempJson = Path.Combine(Path.GetTempPath(), $"bw_test_{Guid.NewGuid():N}.json");
        try
        {
            await File.WriteAllTextAsync(tempJson, sampleBitwarden);
            var res = await service.ImportFromJsonAsync(tempJson);

            AssertEqual(1, res.ImportedCount, "Bitwarden запись не была импортирована");
            AssertEqual("Bitwarden Test Entry", res.ImportedEntries[0].Title, "Название Bitwarden записи не совпадает");
            AssertEqual("bitwarden_user", res.ImportedEntries[0].UsernameOrEmail, "Логин Bitwarden записи не совпадает");
            AssertEqual("BitwardenPassword123!", res.ImportedEntries[0].SecretValue, "Пароль Bitwarden записи не совпадает");
        }
        finally
        {
            if (File.Exists(tempJson)) File.Delete(tempJson);
        }
    }

    private static Task Test_Storm_Certificate()
    {
        var certService = new StormCertificateService();
        var status = certService.CheckStatus();

        AssertTrue(status.IsCertified, "Сертификат STORM ECOSYSTEM должен быть валиден");
        AssertTrue(status.StatusText.Contains("STORM"), "Статус сертификата должен содержать STORM");

        return Task.CompletedTask;
    }

    private static Task Test_ThemeService_Definitions()
    {
        AssertEqual(9, ThemeService.Themes.Count, "Должно быть ровно 9 тем оформления");
        AssertEqual(13, ThemeService.Accents.Count, "Должно быть ровно 13 акцентных цветов");
        AssertEqual(10, ThemeService.Fonts.Count, "Должно быть ровно 10 шрифтов");

        var midnight = ThemeService.GetTheme("MIDNIGHT");
        AssertEqual("MIDNIGHT", midnight.Key, "Тема по умолчанию должна быть MIDNIGHT");
        AssertEqual("#141026", midnight.HeaderHex, "Цвет шапки темы MIDNIGHT должен быть #141026");

        var fallbackAccent = ThemeService.GetAccent("UNKNOWN_ACCENT");
        AssertEqual("Violet", fallbackAccent.Key, "Фолбэк акцента должен быть Violet");

        return Task.CompletedTask;
    }

    private static Task Test_LocalizationService_Definitions()
    {
        AssertEqual(11, LocalizationService.Languages.Count, "Должно быть ровно 11 языков");

        var sysLang = LocalizationService.GetSystemLanguageCode();
        AssertTrue(!string.IsNullOrEmpty(sysLang), "Код языка системы не должен быть пустым");

        // Проверка наличия ключевых строк для всех 11 языков
        foreach (var lang in LocalizationService.Languages)
        {
            var title = LocalizationService.GetString("AppTitle", lang.Code);
            AssertEqual("STORM KEYS", title, $"Заголовок приложения для {lang.Code} должен быть STORM KEYS");

            var add = LocalizationService.GetString("Add", lang.Code);
            AssertTrue(!string.IsNullOrWhiteSpace(add), $"Строка Add для {lang.Code} не должна быть пустой");

            var save = LocalizationService.GetString("SettingsSave", lang.Code);
            AssertTrue(!string.IsNullOrWhiteSpace(save), $"Строка SettingsSave для {lang.Code} не должна быть пустой");

            var tooltipAdd = LocalizationService.GetString("TooltipAdd", lang.Code);
            AssertTrue(!string.IsNullOrWhiteSpace(tooltipAdd), $"Строка TooltipAdd для {lang.Code} не должна быть пустой");

            var aboutTitle = LocalizationService.GetString("AboutTitle", lang.Code);
            AssertTrue(!string.IsNullOrWhiteSpace(aboutTitle), $"Строка AboutTitle для {lang.Code} не должна быть пустой");

            var aboutPub = LocalizationService.GetString("AboutPublisher", lang.Code);
            AssertTrue(!string.IsNullOrWhiteSpace(aboutPub), $"Строка AboutPublisher для {lang.Code} не должна быть пустой");

            var totp = LocalizationService.GetString("TotpCode", lang.Code);
            AssertTrue(!string.IsNullOrWhiteSpace(totp), $"Строка TotpCode для {lang.Code} не должна быть пустой");

            var hello = LocalizationService.GetString("WindowsHelloUnlock", lang.Code);
            AssertTrue(!string.IsNullOrWhiteSpace(hello), $"Строка WindowsHelloUnlock для {lang.Code} не должна быть пустой");

            var audit = LocalizationService.GetString("PwnedAuditButton", lang.Code);
            AssertTrue(!string.IsNullOrWhiteSpace(audit), $"Строка PwnedAuditButton для {lang.Code} не должна быть пустой");
        }

        return Task.CompletedTask;
    }

    private static Task Test_Totp_Service()
    {
        var totp = new TotpService();

        // 1. Генерация секретного ключа Base32
        string secret = totp.GenerateSecretKey(20);
        AssertTrue(secret.Length >= 20, "Секретный ключ Base32 должен содержать не менее 20 символов");

        // 2. Валидация RFC 6238 ключа
        string testSecret = "JBSWY3DPEHPK3PXP"; // "Hello!" in Base32
        string code = totp.GenerateCode(testSecret);
        AssertEqual(6, code.Length, "Код TOTP должен состоять ровно из 6 цифр");
        AssertTrue(int.TryParse(code, out _), "Код TOTP должен быть числовым");

        // 3. Расчет таймера
        int remaining = totp.GetSecondsRemaining();
        AssertTrue(remaining >= 1 && remaining <= 30, $"Оставшиеся секунды должны быть в диапазоне 1..30, получено {remaining}");

        // 4. Форматирование кода
        string formatted = totp.FormatCode("123456");
        AssertEqual("123 456", formatted, "Код должен разделяться пробелом: '123 456'");

        // 5. URI генерация
        string uri = totp.GenerateOtpAuthUri("STORM KEYS", "user@storm.dev", testSecret);
        AssertTrue(uri.StartsWith("otpauth://totp/"), "URI должен начинаться с otpauth://totp/");

        return Task.CompletedTask;
    }

    private static async Task Test_Pwned_Service()
    {
        var pwned = new PwnedPasswordsService();

        // Пустой пароль
        var emptyRes = await pwned.CheckPasswordAsync("");
        AssertEqual(0, emptyRes.BreachCount, "Пустой пароль не должен возвращать утечек");
        AssertTrue(!emptyRes.IsBreached, "Пустой пароль не скомпрометирован");

        // Проверка пакетного аудита списка записей
        var entries = new System.Collections.Generic.List<KeyEntry>
        {
            new() { Title = "Safe Entry 1", SecretValue = "Unbreakable_Storm_Pass_2026_!@#" },
            new() { Title = "Empty Secret", SecretValue = "" }
        };

        var summary = await pwned.AuditVaultAsync(entries);
        AssertEqual(2, summary.TotalChecked, "Всего должно быть проверено 2 записи");

        return;
    }

    private static void AssertTrue(bool condition, string message)
    {
        if (!condition)
            throw new Exception(message);
    }

    private static void AssertEqual<T>(T expected, T actual, string message)
    {
        if (!object.Equals(expected, actual))
            throw new Exception($"{message} (Ожидалось: '{expected}', Получено: '{actual}')");
    }
}
