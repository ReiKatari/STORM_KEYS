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

        byte[] encrypted = await crypto.EncryptVaultAsync(container, "CorrectPassword123");
        bool verified = await crypto.VerifyMasterPasswordAsync(encrypted, "WrongPassword456");

        AssertTrue(!verified, "Неверный пароль не был отклонен!");
    }

    private static async Task Test_Tampering_Detection()
    {
        var crypto = new CryptoService();
        var container = new VaultContainer
        {
            Entries = new() { new KeyEntry { Title = "Secret", SecretValue = "12345" } }
        };

        byte[] encrypted = await crypto.EncryptVaultAsync(container, "MasterPass999");

        // Повреждаем байт в шифртексте
        encrypted[encrypted.Length - 1] ^= 0xFF;

        bool verified = await crypto.VerifyMasterPasswordAsync(encrypted, "MasterPass999");
        AssertTrue(!verified, "Модифицированный файл не был распознан как поврежденный!");
    }

    private static Task Test_Expiration_Calculations()
    {
        var entry1 = new KeyEntry
        {
            Title = "Active Key",
            ExpirationDate = DateTimeOffset.Now.AddDays(10)
        };
        AssertTrue(entry1.IsExpiringSoon, "Запись за 10 дней должна быть IsExpiringSoon");
        AssertTrue(!entry1.IsExpired, "Запись за 10 дней не должна быть IsExpired");
        AssertEqual(10, entry1.DaysRemaining, "Неверный расчет оставшихся дней");

        var entry2 = new KeyEntry
        {
            Title = "Expired Key",
            ExpirationDate = DateTimeOffset.Now.AddDays(-5)
        };
        AssertTrue(entry2.IsExpired, "Запись с -5 дней должна быть IsExpired");
        AssertTrue(!entry2.IsExpiringSoon, "Истёкшая запись не должна быть IsExpiringSoon");

        var entry3 = new KeyEntry
        {
            Title = "Lifetime Key",
            ExpirationDate = null
        };
        AssertTrue(!entry3.HasExpiration, "Бессрочная запись не должна иметь expiration");
        AssertEqual("Бессрочно", entry3.ExpirationStatusText, "Неверный текст бессрочного статуса");

        return Task.CompletedTask;
    }

    private static Task Test_Password_License_Generator()
    {
        string pass = PasswordGenerator.Generate(24);
        AssertEqual(24, pass.Length, "Длина сгенерированного пароля не совпадает");

        string license = PasswordGenerator.GenerateLicenseKey();
        AssertEqual(29, license.Length, "Длина лицензионного ключа не 29 символов (5x5)");
        AssertTrue(license.Contains("-"), "Лицензионный ключ не содержит разделителей '-'");

        var eval = PasswordGenerator.EvaluateStrength("P@ssw0rdVeryStrong!2026#Secure");
        AssertTrue(eval.Score >= 3, "Сложный пароль оценен слишком низко");

        return Task.CompletedTask;
    }

    private static Task Test_Clipboard_Import()
    {
        var importExport = new ImportExportService();

        string clipLicense = "Ваш лицензионный ключ: XXXXX-YYYYY-ZZZZZ-AAAAA-BBBBB";
        var res1 = importExport.ImportFromClipboardText(clipLicense);
        AssertEqual(1, res1.ImportedCount, "Не удалось распознать лицензионный ключ из текста");
        AssertEqual(KeyCategory.License, res1.ImportedEntries[0].Category, "Неверная категория ключа");
        AssertEqual("XXXXX-YYYYY-ZZZZZ-AAAAA-BBBBB", res1.ImportedEntries[0].SecretValue, "Неверный ключ");

        string clipCreds = "Login: dev_user\nPassword: DevPassword123!\nURL: https://storm.keys";
        var res2 = importExport.ImportFromClipboardText(clipCreds);
        AssertEqual(1, res2.ImportedCount, "Не удалось распознать пару login/password из текста");
        AssertEqual("dev_user", res2.ImportedEntries[0].UsernameOrEmail, "Неверный логин");
        AssertEqual("DevPassword123!", res2.ImportedEntries[0].SecretValue, "Неверный пароль");

        return Task.CompletedTask;
    }

    private static async Task Test_Csv_Export_Import_RoundTrip()
    {
        var importExport = new ImportExportService();
        string tempCsv = Path.Combine(Path.GetTempPath(), $"storm_test_{Guid.NewGuid():N}.csv");

        try
        {
            var entries = new[]
            {
                new KeyEntry
                {
                    Title = "Cloud Service",
                    UsernameOrEmail = "user1@cloud.com",
                    SecretValue = "Secr3t#Key",
                    TargetAppOrUrl = "https://cloud.com",
                    ExpirationDate = DateTimeOffset.Now.AddDays(45),
                    Notes = "Sample notes"
                }
            };

            await importExport.ExportToCsvAsync(entries, tempCsv);
            AssertTrue(File.Exists(tempCsv), "CSV файл не был создан");

            var importResult = await importExport.ImportFromCsvAsync(tempCsv);
            AssertEqual(1, importResult.ImportedCount, "Импорт CSV вернул неверное количество");
            AssertEqual("Cloud Service", importResult.ImportedEntries[0].Title, "Заголовок искажен");
            AssertEqual("user1@cloud.com", importResult.ImportedEntries[0].UsernameOrEmail, "Логин искажен");
            AssertEqual("Secr3t#Key", importResult.ImportedEntries[0].SecretValue, "Секрет искажен");
        }
        finally
        {
            if (File.Exists(tempCsv))
                File.Delete(tempCsv);
        }
    }

    private static async Task Test_Json_Import()
    {
        var importExport = new ImportExportService();
        string tempJson = Path.Combine(Path.GetTempPath(), $"storm_bitwarden_{Guid.NewGuid():N}.json");

        try
        {
            string bwJson = @"{
                ""encrypted"": false,
                ""items"": [
                    {
                        ""name"": ""Bitwarden Vault Entry"",
                        ""notes"": ""Test notes"",
                        ""login"": {
                            ""username"": ""tester@bitwarden.com"",
                            ""password"": ""MasterPassBW123"",
                            ""uris"": [ { ""uri"": ""https://bitwarden.com"" } ]
                        }
                    }
                ]
            }";

            await File.WriteAllTextAsync(tempJson, bwJson);

            var res = await importExport.ImportFromJsonAsync(tempJson);
            AssertEqual(1, res.ImportedCount, "Не удалось импортировать Bitwarden JSON");
            AssertEqual("Bitwarden Vault Entry", res.ImportedEntries[0].Title, "Неверное имя");
            AssertEqual("tester@bitwarden.com", res.ImportedEntries[0].UsernameOrEmail, "Неверный логин");
            AssertEqual("MasterPassBW123", res.ImportedEntries[0].SecretValue, "Неверный пароль");
        }
        finally
        {
            if (File.Exists(tempJson))
                File.Delete(tempJson);
        }
    }

    private static Task Test_Storm_Certificate()
    {
        var certService = new StormCertificateService();
        var status = certService.CheckStatus();
        AssertTrue(status.IsCertified, "Статус сертификации должен быть подтвержден");
        AssertTrue(!string.IsNullOrEmpty(status.StatusText), "Текст статуса пуст");
        return Task.CompletedTask;
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
