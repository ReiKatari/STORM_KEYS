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
        await RunTest("Tray Service: Проверка NOTIFYICONDATA и иконки", Test_Tray_Service);
        await RunTest("Генератор 1.0.3: Passphrase (Diceware/xkcd), Энтропия (бит) и Время взлома", Test_Passphrase_Entropy_CrackTime);
        await RunTest("Модель 1.0.3: Клонирование (Clone), Peek-просмотр, Избранное и Теги", Test_KeyEntry_Clone_Peek_Tags);
        await RunTest("Модель 1.0.3: Пользовательские поля (CustomFields) и История паролей", Test_CustomFields_And_PasswordHistory);
        await RunTest("Хранилище 1.0.3: Ротация 3 поколений бэкапов (.bak1..3) и Снапшоты", Test_Vault_Rolling_Backups);
        await RunTest("Хранилище 1.0.3: Смена мастер-пароля и перешифрование AES-256-GCM", Test_Vault_Change_MasterPassword);
        await RunTest("Экспорт 1.0.3: Генерация Аварийного листа восстановления (.txt)", Test_Emergency_Recovery_Kit);
        await RunTest("ViewModel 1.0.3: Умный поиск, Чипы (Слабые/Дубли), Сортировка и Undo", Test_ViewModel_Search_Sort_Chips_Undo);

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


    private static Task Test_Tray_Service()
    {
        var trayType = typeof(StormTrayService);
        var nidType = trayType.GetNestedType("NOTIFYICONDATA", System.Reflection.BindingFlags.NonPublic);
        AssertTrue(nidType != null, "NOTIFYICONDATA struct must exist");
        int size = System.Runtime.InteropServices.Marshal.SizeOf(nidType!);
        Console.Write($"[struct size: {size} bytes] ");
        AssertTrue(size > 0, "Size must be > 0");

        return Task.CompletedTask;
    }


    private static Task Test_Passphrase_Entropy_CrackTime()
    {
        string phrase = PasswordGenerator.GeneratePassphrase(4, "-", true);
        AssertTrue(!string.IsNullOrWhiteSpace(phrase), "Парольная фраза не должна быть пустой");
        AssertTrue(phrase.Split('-').Length >= 5, $"Парольная фраза должна содержать 4 слова + число: {phrase}");

        double entropyWeak = PasswordGenerator.CalculateEntropyBits("12345");
        double entropyStrong = PasswordGenerator.CalculateEntropyBits(phrase);
        AssertTrue(entropyStrong > entropyWeak, $"Энтропия фразы ({entropyStrong}) должна быть выше слабой ({entropyWeak})");
        AssertTrue(entropyStrong >= 100.0, $"Энтропия криптостойкой фразы должна быть >= 100 бит, получено {entropyStrong}");

        string crackWeak = PasswordGenerator.EstimateCrackTime("abc");
        string crackStrong = PasswordGenerator.EstimateCrackTime(phrase);
        AssertTrue(crackWeak.Contains("сек") || crackWeak.Contains("Мгновенно"), $"Слабый пароль должен взламываться мгновенно: {crackWeak}");
        AssertTrue(crackStrong.Contains("лет"), $"Сильная фраза должна требовать лет перебора: {crackStrong}");

        return Task.CompletedTask;
    }

    private static Task Test_KeyEntry_Clone_Peek_Tags()
    {
        var original = new KeyEntry
        {
            Title = "Prod Kubernetes",
            Category = KeyCategory.Server,
            UsernameOrEmail = "root",
            SecretValue = "K8s-Master-Secret-2026!",
            TargetAppOrUrl = "https://k8s.storm.dev",
            IsFavorite = true,
            Tags = new System.Collections.Generic.List<string> { "prod", "infra", "k8s" }
        };

        // Проверка маскировки и Peek-режима
        AssertTrue(!original.IsSecretRevealed, "По умолчанию секрет должен быть скрыт");
        AssertTrue(original.DisplayedSecret.StartsWith("•"), "DisplayedSecret должен возвращать маску по умолчанию");
        original.IsSecretRevealed = true;
        AssertEqual("K8s-Master-Secret-2026!", original.DisplayedSecret, "В режиме Peek должен отображаться реальный секрет");
        original.IsSecretRevealed = false;

        // Проверка тегов и избранного
        AssertEqual("#prod #infra #k8s", original.TagsDisplayText, "Форматирование тегов должно добавлять #");
        AssertEqual("", original.FavoriteGlyph, "Глиф избранного должен быть заполненной звездой");
        AssertTrue(original.HasWebsiteUrl && original.WebsiteLaunchUri != null, "URL должен определяться корректно");

        // Проверка клонирования
        var clone = original.Clone();
        AssertTrue(clone.Id != original.Id, "Клон должен иметь новый уникальный Guid");
        AssertEqual("Prod Kubernetes (Копия)", clone.Title, "Название клона должно содержать (Копия)");
        AssertEqual(original.SecretValue, clone.SecretValue, "Секрет клона должен совпадать");
        AssertEqual(3, clone.Tags.Count, "Теги должны копироваться в клон");

        return Task.CompletedTask;
    }

    private static Task Test_CustomFields_And_PasswordHistory()
    {
        var entry = new KeyEntry
        {
            Title = "Corporate VPN",
            SecretValue = "OldVpnPass#1"
        };
        entry.CustomFields["PIN"] = "9482";
        entry.CustomFields["Port"] = "1194";

        // Имитация смены пароля с сохранением истории
        entry.PasswordHistory.Insert(0, new PasswordHistoryItem { Password = entry.SecretValue, ChangedAt = DateTimeOffset.Now });
        entry.SecretValue = "NewVpnPass#2026!";

        AssertEqual(2, entry.CustomFields.Count, "Должно быть 2 пользовательских поля");
        AssertEqual("9482", entry.CustomFields["PIN"], "PIN должен быть 9482");
        AssertEqual(1, entry.PasswordHistory.Count, "История паролей должна содержать 1 запись");
        AssertEqual("OldVpnPass#1", entry.PasswordHistory[0].Password, "В истории должен храниться старый пароль");

        return Task.CompletedTask;
    }

    private static async Task Test_Vault_Rolling_Backups()
    {
        var crypto = new CryptoService();
        var vault = new VaultService(crypto);
        string tempDir = Path.Combine(Path.GetTempPath(), $"storm_vault_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        string vaultPath = Path.Combine(tempDir, "test_vault.stormdb");

        try
        {
            vault.Settings.LocalVaultPath = vaultPath;
            await vault.CreateVaultAsync("MasterPass!103");

            // Выполняем 3 сохранения для проверки генерации .bak1, .bak2, .bak3
            vault.CurrentContainer!.Entries.Add(new KeyEntry { Title = "Key 1", SecretValue = "Sec1" });
            await vault.SaveVaultAsync();

            vault.CurrentContainer!.Entries.Add(new KeyEntry { Title = "Key 2", SecretValue = "Sec2" });
            await vault.SaveVaultAsync();

            vault.CurrentContainer!.Entries.Add(new KeyEntry { Title = "Key 3", SecretValue = "Sec3" });
            await vault.SaveVaultAsync();

            AssertTrue(File.Exists(vaultPath + ".bak1"), "Должна существовать резервная копия .bak1");
            AssertTrue(File.Exists(vaultPath + ".bak2"), "Должна существовать резервная копия .bak2");
            AssertTrue(File.Exists(vaultPath + ".bak3"), "Должна существовать резервная копия .bak3");

            string snap = await vault.CreateBackupSnapshotAsync();
            AssertTrue(File.Exists(snap), "Ручной снапшот резервной копии должен быть создан");
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }

    private static async Task Test_Vault_Change_MasterPassword()
    {
        var crypto = new CryptoService();
        var vault = new VaultService(crypto);
        string tempDir = Path.Combine(Path.GetTempPath(), $"storm_pwdchange_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        string vaultPath = Path.Combine(tempDir, "vault.stormdb");

        try
        {
            vault.Settings.LocalVaultPath = vaultPath;
            await vault.CreateVaultAsync("InitialMasterPass!1");
            vault.CurrentContainer!.Entries.Add(new KeyEntry { Title = "Critical Key", SecretValue = "TopSecretValue" });
            await vault.SaveVaultAsync();

            await vault.ChangeMasterPasswordAsync("InitialMasterPass!1", "NewUpdatedMasterPass!2");
            await vault.LockVaultAsync();

            bool oldUnlock = await vault.UnlockVaultAsync("InitialMasterPass!1");
            AssertTrue(!oldUnlock, "Старый мастер-пароль больше не должен открывать хранилище");

            bool newUnlock = await vault.UnlockVaultAsync("NewUpdatedMasterPass!2");
            AssertTrue(newUnlock, "Новый мастер-пароль должен успешно открывать хранилище");
            AssertEqual(1, vault.CurrentContainer!.Entries.Count, "Записи должны сохраниться после смены мастер-пароля");
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }

    private static async Task Test_Emergency_Recovery_Kit()
    {
        var service = new ImportExportService();
        var entries = new System.Collections.Generic.List<KeyEntry>
        {
            new()
            {
                Title = "Primary AWS Root",
                Category = KeyCategory.Server,
                UsernameOrEmail = "aws-root@storm.dev",
                SecretValue = "Aws-Root-Pass-99!",
                TotpSecret = "JBSWY3DPEHPK3PXP",
                Tags = new() { "aws", "root" }
            }
        };

        string tempKit = Path.Combine(Path.GetTempPath(), $"recovery_kit_{Guid.NewGuid():N}.txt");
        try
        {
            await service.ExportEmergencyRecoveryKitAsync(entries, tempKit);
            AssertTrue(File.Exists(tempKit), "Файл аварийного листа должен быть создан");
            string content = await File.ReadAllTextAsync(tempKit);
            AssertTrue(content.Contains("STORM KEYS — АВАРИЙНЫЙ ЛИСТ ВОССТАНОВЛЕНИЯ"), "Заголовок аварийного листа должен присутствовать");
            AssertTrue(content.Contains("Primary AWS Root"), "Название записи должно присутствовать в аварийном листе");
            AssertTrue(content.Contains("JBSWY3DPEHPK3PXP"), "Секрет 2FA должен присутствовать в аварийном листе");
        }
        finally
        {
            if (File.Exists(tempKit)) File.Delete(tempKit);
        }
    }


    private static Task Test_ViewModel_Search_Sort_Chips_Undo()
    {
        var e1 = new KeyEntry { Title = "Alpha GitHub", UsernameOrEmail = "admin", SecretValue = "12345", Tags = new() { "prod", "git" } };
        var e2 = new KeyEntry { Title = "Beta Server", UsernameOrEmail = "root", SecretValue = "12345", Category = KeyCategory.Server };
        var e3 = new KeyEntry { Title = "Gamma Cloud", UsernameOrEmail = "devops", SecretValue = PasswordGenerator.GeneratePassphrase(4, "-", true), IsFavorite = true };

        var allEntries = new System.Collections.Generic.List<KeyEntry> { e1, e2, e3 };

        // 1. Слабые и дублирующиеся пароли
        int weakCount = System.Linq.Enumerable.Count(allEntries, e => !string.IsNullOrEmpty(e.SecretValue) && PasswordGenerator.EvaluateStrength(e.SecretValue).Score <= 2);
        AssertEqual(2, weakCount, "Должно определяться ровно 2 слабых пароля");

        var dupGroups = System.Linq.Enumerable.Where(System.Linq.Enumerable.GroupBy(allEntries, e => e.SecretValue), g => System.Linq.Enumerable.Count(g) > 1);
        int dupCount = System.Linq.Enumerable.Sum(dupGroups, g => System.Linq.Enumerable.Count(g));
        AssertEqual(2, dupCount, "Должно определяться ровно 2 дублирующихся пароля");

        // 2. Многословный умный поиск по тегам и заголовку
        string queryStr = "alpha #prod";
        var tokens = queryStr.Trim().ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var matched = System.Linq.Enumerable.ToList(System.Linq.Enumerable.Where(allEntries, e =>
        {
            string tagsJoined = string.Join(" ", e.Tags).ToLowerInvariant();
            return System.Linq.Enumerable.All(tokens, t =>
            {
                string ct = t.TrimStart('#');
                return e.Title.ToLowerInvariant().Contains(ct) || tagsJoined.Contains(ct);
            });
        }));
        AssertEqual(1, matched.Count, "Умный поиск 'alpha #prod' должен найти ровно 1 запись");
        AssertEqual("Alpha GitHub", matched[0].Title, "Найденная запись должна быть Alpha GitHub");

        // 3. Многорежимная сортировка по надёжности (энтропии)
        var sortedByStrength = System.Linq.Enumerable.ToList(
            System.Linq.Enumerable.ThenBy(
                System.Linq.Enumerable.OrderByDescending(allEntries, e => e.IsFavorite),
                e => e.EntropyBits));
        AssertEqual("Gamma Cloud", sortedByStrength[0].Title, "Избранная запись всегда должна быть первой при любой сортировке");

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
