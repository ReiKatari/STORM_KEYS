using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Linq;
using StormKeys.Models;

namespace StormKeys.Services;

public class ImportResult
{
    public int ImportedCount { get; set; }
    public int SkippedCount { get; set; }
    public List<KeyEntry> ImportedEntries { get; set; } = new();
    public string FormatDetected { get; set; } = string.Empty;
}

public interface IImportExportService
{
    Task<ImportResult> ImportFromCsvAsync(string filePath);
    Task<ImportResult> ImportFromJsonAsync(string filePath);
    Task<ImportResult> ImportFromKeePassXmlAsync(string filePath);
    Task<ImportResult> ImportEncryptedBackupAsync(string filePath, string backupPassword);
    ImportResult ImportFromClipboardText(string text);
    Task ExportToCsvAsync(IEnumerable<KeyEntry> entries, string filePath);
    Task ExportToJsonAsync(IEnumerable<KeyEntry> entries, string filePath);
    Task ExportToDotEnvAsync(IEnumerable<KeyEntry> entries, string filePath);
    Task ExportEncryptedBackupAsync(IEnumerable<KeyEntry> entries, string filePath, string backupPassword);
    Task ExportEmergencyRecoveryKitAsync(IEnumerable<KeyEntry> entries, string filePath);
    Task ExportToPrintableHtmlAsync(IEnumerable<KeyEntry> entries, string filePath);
}

public class ImportExportService : IImportExportService
{
    public async Task<ImportResult> ImportFromCsvAsync(string filePath)
    {
        var result = new ImportResult();
        if (!File.Exists(filePath))
            return result;

        // Если это .xml файл KeePass 2.x — парсим через XML-импортер
        if (filePath.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
        {
            return await ImportFromKeePassXmlAsync(filePath);
        }

        // Если это .env или .txt файл без CSV-заголовка, парсим через универсальный анализатор
        if (filePath.EndsWith(".env", StringComparison.OrdinalIgnoreCase) ||
            filePath.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
        {
            string rawText = await File.ReadAllTextAsync(filePath, Encoding.UTF8);
            return ImportFromClipboardText(rawText);
        }

        var lines = await File.ReadAllLinesAsync(filePath, Encoding.UTF8);

        if (lines.Length <= 1)
        {
            if (lines.Length == 1)
                return ImportFromClipboardText(lines[0]);
            return result;
        }

        string headerLine = lines[0].Trim();
        var headers = ParseCsvRow(headerLine);

        int nameIdx = -1, urlIdx = -1, userIdx = -1, passIdx = -1, noteIdx = -1, totpIdx = -1;

        for (int i = 0; i < headers.Count; i++)
        {
            string h = headers[i].ToLowerInvariant().Trim();
            if (h.Contains("totp") || h.Contains("2fa") || h.Contains("otp"))
                totpIdx = i;
            else if (h.Contains("user") || h.Contains("login") || h.Contains("email"))
                userIdx = i;
            else if (h.Contains("pass") || h.Contains("secret") || (h.Contains("key") && !h.Contains("category")))
                passIdx = i;
            else if (h.Contains("url") || h.Contains("uri") || h.Contains("web") || h.Contains("host"))
                urlIdx = i;
            else if (h == "title" || h == "name" || h.Contains("title") || h.Contains("service") || h.Contains("app"))
                nameIdx = i;
            else if (h.Contains("note") || h.Contains("comment") || h.Contains("desc"))
                noteIdx = i;
            else if (h.Contains("name") && !h.Contains("user"))
                nameIdx = i;
        }

        if (nameIdx == -1 && urlIdx != -1) nameIdx = urlIdx;

        result.FormatDetected = "CSV (Браузер / Менеджер паролей)";

        for (int i = 1; i < lines.Length; i++)
        {
            string line = lines[i].Trim();
            if (string.IsNullOrWhiteSpace(line)) continue;

            var cols = ParseCsvRow(line);
            if (cols.Count == 0) continue;

            string title = nameIdx >= 0 && nameIdx < cols.Count ? cols[nameIdx] : "Без названия";
            string url = urlIdx >= 0 && urlIdx < cols.Count ? cols[urlIdx] : "";
            string user = userIdx >= 0 && userIdx < cols.Count ? cols[userIdx] : "";
            string pass = passIdx >= 0 && passIdx < cols.Count ? cols[passIdx] : "";
            string notes = noteIdx >= 0 && noteIdx < cols.Count ? cols[noteIdx] : "";
            string totp = totpIdx >= 0 && totpIdx < cols.Count ? cols[totpIdx] : "";

            if (string.IsNullOrWhiteSpace(title) && !string.IsNullOrWhiteSpace(url))
                title = CleanUrlToTitle(url);

            if (string.IsNullOrWhiteSpace(user) && string.IsNullOrWhiteSpace(pass) && string.IsNullOrWhiteSpace(totp))
            {
                result.SkippedCount++;
                continue;
            }

            var entry = new KeyEntry
            {
                Title = string.IsNullOrWhiteSpace(title) ? "Импортированная запись" : title,
                TargetAppOrUrl = url,
                UsernameOrEmail = user,
                SecretValue = pass,
                TotpSecret = totp,
                Notes = notes,
                Category = KeyCategory.Account,
                CreatedAt = DateTimeOffset.UtcNow,
                ModifiedAt = DateTimeOffset.UtcNow
            };

            result.ImportedEntries.Add(entry);
            result.ImportedCount++;
        }

        return result;
    }

    public async Task<ImportResult> ImportFromJsonAsync(string filePath)
    {
        var result = new ImportResult();
        string json = await File.ReadAllTextAsync(filePath, Encoding.UTF8);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        // 1. Bitwarden JSON
        if (root.TryGetProperty("items", out var itemsElement) && itemsElement.ValueKind == JsonValueKind.Array)
        {
            result.FormatDetected = "Bitwarden JSON";
            foreach (var item in itemsElement.EnumerateArray())
            {
                string name = item.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                string notes = item.TryGetProperty("notes", out var nt) ? nt.GetString() ?? "" : "";
                string username = "";
                string password = "";
                string totp = "";
                string uri = "";

                if (item.TryGetProperty("login", out var loginElem))
                {
                    if (loginElem.TryGetProperty("username", out var u)) username = u.GetString() ?? "";
                    if (loginElem.TryGetProperty("password", out var p)) password = p.GetString() ?? "";
                    if (loginElem.TryGetProperty("totp", out var t)) totp = t.GetString() ?? "";
                    if (loginElem.TryGetProperty("uris", out var urisElem) && urisElem.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var uObj in urisElem.EnumerateArray())
                        {
                            if (uObj.TryGetProperty("uri", out var uriVal))
                            {
                                uri = uriVal.GetString() ?? "";
                                break;
                            }
                        }
                    }
                }

                if (!string.IsNullOrWhiteSpace(username) || !string.IsNullOrWhiteSpace(password) || !string.IsNullOrWhiteSpace(name))
                {
                    result.ImportedEntries.Add(new KeyEntry
                    {
                        Title = string.IsNullOrWhiteSpace(name) ? CleanUrlToTitle(uri) : name,
                        UsernameOrEmail = username,
                        SecretValue = password,
                        TotpSecret = totp,
                        TargetAppOrUrl = uri,
                        Notes = notes,
                        Category = KeyCategory.Account
                    });
                    result.ImportedCount++;
                }
            }
            return result;
        }

        // 2. Generic JSON массив объектов KeyEntry или логинов
        if (root.ValueKind == JsonValueKind.Array)
        {
            result.FormatDetected = "JSON Список";
            foreach (var item in root.EnumerateArray())
            {
                string title = GetPropString(item, "Title", "name", "title") ?? "Без названия";
                string user = GetPropString(item, "UsernameOrEmail", "username", "user", "login", "email") ?? "";
                string secret = GetPropString(item, "SecretValue", "password", "secret", "key") ?? "";
                string target = GetPropString(item, "TargetAppOrUrl", "url", "target", "app") ?? "";
                string notes = GetPropString(item, "Notes", "notes", "note") ?? "";
                string totp = GetPropString(item, "TotpSecret", "totp", "otp") ?? "";
                string customIcon = GetPropString(item, "CustomIconGlyph") ?? "";

                if (!string.IsNullOrWhiteSpace(user) || !string.IsNullOrWhiteSpace(secret) || !string.IsNullOrWhiteSpace(totp))
                {
                    result.ImportedEntries.Add(new KeyEntry
                    {
                        Title = title,
                        UsernameOrEmail = user,
                        SecretValue = secret,
                        TotpSecret = totp,
                        CustomIconGlyph = customIcon,
                        TargetAppOrUrl = target,
                        Notes = notes,
                        Category = KeyCategory.Account
                    });
                    result.ImportedCount++;
                }
            }
        }

        return result;
    }

    public ImportResult ImportFromClipboardText(string text)
    {
        var result = new ImportResult { FormatDetected = "Буфер обмена" };
        if (string.IsNullOrWhiteSpace(text))
            return result;

        string trimmed = text.Trim();
        var totpSvc = new TotpService();

        // 1. Формат otpauth://totp/... (QR/URI 2FA)
        if (trimmed.StartsWith("otpauth://", StringComparison.OrdinalIgnoreCase) &&
            totpSvc.TryParseOtpAuthInput(trimmed, out string cleanSecret, out string? issuer, out string? account))
        {
            result.FormatDetected = "URI 2FA (otpauth://)";
            result.ImportedEntries.Add(new KeyEntry
            {
                Title = !string.IsNullOrWhiteSpace(issuer) ? issuer : (!string.IsNullOrWhiteSpace(account) ? account : "2FA Токен"),
                UsernameOrEmail = account ?? string.Empty,
                TotpSecret = cleanSecret,
                Category = KeyCategory.Account,
                Notes = "Импортировано из URI двухфакторной аутентификации (otpauth://)"
            });
            result.ImportedCount++;
            return result;
        }

        // 2. Формат URL с учётными данными: https://user:pass@example.com или ssh://user:pass@host:22
        if ((trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
             trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
             trimmed.StartsWith("ssh://", StringComparison.OrdinalIgnoreCase) ||
             trimmed.StartsWith("ftp://", StringComparison.OrdinalIgnoreCase) ||
             trimmed.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase) ||
             trimmed.StartsWith("mysql://", StringComparison.OrdinalIgnoreCase)) &&
            Uri.TryCreate(trimmed, UriKind.Absolute, out var credUri) &&
            !string.IsNullOrWhiteSpace(credUri.UserInfo))
        {
            var up = credUri.UserInfo.Split(':', 2);
            string uName = Uri.UnescapeDataString(up[0]);
            string uPass = up.Length > 1 ? Uri.UnescapeDataString(up[1]) : string.Empty;
            bool isServer = credUri.Scheme is "ssh" or "ftp" or "postgres" or "mysql";
            result.FormatDetected = "URI с учётными данными";
            result.ImportedEntries.Add(new KeyEntry
            {
                Title = credUri.Host,
                UsernameOrEmail = uName,
                SecretValue = uPass,
                TargetAppOrUrl = $"{credUri.Scheme}://{credUri.Authority}{credUri.AbsolutePath}",
                Category = isServer ? KeyCategory.Server : KeyCategory.Account
            });
            result.ImportedCount++;
            return result;
        }

        if (text.Trim().StartsWith("stormshare://", StringComparison.OrdinalIgnoreCase))
        {
            var shared = TryDecodeSecureShareToken(text.Trim(), "STORM");
            if (shared != null)
            {
                result.FormatDetected = "STORM Secure Share Token (AES-256-GCM)";
                result.ImportedEntries.Add(shared);
                result.ImportedCount = 1;
                return result;
            }
        }

        string[] lines = text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries);

        // 3. Проверяем табличный формат с табуляцией (\t)
        bool hasTabs = false;
        foreach (var l in lines)
        {
            if (l.Contains('\t'))
            {
                hasTabs = true;
                break;
            }
        }

        if (hasTabs)
        {
            foreach (var rawLine in lines)
            {
                var parts = rawLine.Split('\t');
                if (parts.Length >= 2)
                {
                    string p0 = parts[0].Trim();
                    string p1 = parts[1].Trim();
                    string p2 = parts.Length > 2 ? parts[2].Trim() : "";

                    string rowTitle = "Запись из буфера";
                    string rowUser = "";
                    string rowPass = "";
                    string rowUrl = "";

                    if (parts.Length == 2)
                    {
                        if (p0.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                        {
                            rowUrl = p0;
                            rowPass = p1;
                            try { rowTitle = new Uri(p0).Host; } catch { }
                        }
                        else
                        {
                            rowUser = p0;
                            rowPass = p1;
                        }
                    }
                    else
                    {
                        if (p0.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                        {
                            rowUrl = p0;
                            rowUser = p1;
                            rowPass = p2;
                            try { rowTitle = new Uri(p0).Host; } catch { }
                        }
                        else
                        {
                            rowTitle = p0;
                            rowUser = p1;
                            rowPass = p2;
                        }
                    }

                    if (!string.IsNullOrEmpty(rowUser) || !string.IsNullOrEmpty(rowPass))
                    {
                        result.ImportedEntries.Add(new KeyEntry
                        {
                            Title = string.IsNullOrEmpty(rowTitle) ? "Запись из буфера" : rowTitle,
                            UsernameOrEmail = rowUser,
                            SecretValue = rowPass,
                            TargetAppOrUrl = rowUrl,
                            Category = KeyCategory.Account
                        });
                        result.ImportedCount++;
                    }
                }
            }

            if (result.ImportedCount > 0)
                return result;
        }

        // 4. Проверяем паттерн "Ключ активации": XXXXX-XXXXX-XXXXX-XXXXX-XXXXX (или 4 группы)
        var licenseRegex = new Regex(@"([A-Z0-9]{4,5}-[A-Z0-9]{4,5}-[A-Z0-9]{4,5}-[A-Z0-9]{4,5}(-[A-Z0-9]{4,5})?)", RegexOptions.IgnoreCase);
        var match = licenseRegex.Match(text);
        if (match.Success)
        {
            result.FormatDetected = "Лицензионный ключ ПО";
            result.ImportedEntries.Add(new KeyEntry
            {
                Title = "Лицензионный ключ из буфера",
                SecretValue = match.Value.ToUpperInvariant(),
                Category = KeyCategory.License,
                Notes = text
            });
            result.ImportedCount++;
            return result;
        }

        // 5. Проверяем формат .env переменных (KEY=VALUE)
        var envEntries = new List<KeyEntry>();
        foreach (var rawLine in lines)
        {
            string l = rawLine.Trim();
            if (l.StartsWith('#') || !l.Contains('=')) continue;
            int eqIdx = l.IndexOf('=');
            if (eqIdx > 1 && eqIdx < l.Length - 1)
            {
                string envKey = l[..eqIdx].Trim();
                string envVal = l[(eqIdx + 1)..].Trim().Trim('"', '\'');
                if (Regex.IsMatch(envKey, @"^[A-Z0-9_]{3,}$") && !string.IsNullOrWhiteSpace(envVal))
                {
                    envEntries.Add(new KeyEntry
                    {
                        Title = envKey,
                        UsernameOrEmail = envKey,
                        SecretValue = envVal,
                        Category = KeyCategory.ApiKey,
                        Notes = "Импортировано из переменной окружения (.env)"
                    });
                }
            }
        }
        if (envEntries.Count > 0)
        {
            result.FormatDetected = "Переменные .env / API Ключи";
            result.ImportedEntries.AddRange(envEntries);
            result.ImportedCount = envEntries.Count;
            return result;
        }

        // 5B. Проверяем многозаписный формат Kaspersky Password Manager (.txt блок: Website name / Login / Password, разделённый "---")
        if (text.Contains("Website name:", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("Login name:", StringComparison.OrdinalIgnoreCase) ||
            (text.Contains("---") && lines.Length >= 4))
        {
            var blocks = text.Split(new[] { "---", "===" }, StringSplitOptions.RemoveEmptyEntries);
            if (blocks.Length >= 2)
            {
                var kpmEntries = new List<KeyEntry>();
                foreach (var block in blocks)
                {
                    string bTitle = "";
                    string bUser = "";
                    string bPass = "";
                    string bUrl = "";
                    string bNotes = "";
                    foreach (var bl in block.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        int cIdx = bl.IndexOf(':');
                        if (cIdx <= 0) continue;
                        string k = bl[..cIdx].Trim().ToLowerInvariant();
                        string v = bl[(cIdx + 1)..].Trim();
                        if (string.IsNullOrEmpty(v)) continue;

                        if (k.Contains("website name") || k.Contains("application") || k.Contains("account name") || k == "title" || k == "название")
                            bTitle = v;
                        else if (k.Contains("website url") || k == "url" || k == "сайт")
                            bUrl = v;
                        else if (k.Contains("login") || k.Contains("user") || k.Contains("email") || k == "логин")
                            bUser = v;
                        else if (k.Contains("password") || k.Contains("пароль"))
                            bPass = v;
                        else if (k.Contains("comment") || k.Contains("note"))
                            bNotes = v;
                    }

                    if (!string.IsNullOrWhiteSpace(bUser) || !string.IsNullOrWhiteSpace(bPass))
                    {
                        kpmEntries.Add(new KeyEntry
                        {
                            Title = string.IsNullOrWhiteSpace(bTitle) ? (string.IsNullOrWhiteSpace(bUrl) ? "Запись Kaspersky" : CleanUrlToTitle(bUrl)) : bTitle,
                            UsernameOrEmail = bUser,
                            SecretValue = bPass,
                            TargetAppOrUrl = bUrl,
                            Notes = bNotes,
                            Category = KeyCategory.Account
                        });
                    }
                }

                if (kpmEntries.Count > 0)
                {
                    result.FormatDetected = "Kaspersky Password Manager (.txt)";
                    result.ImportedEntries.AddRange(kpmEntries);
                    result.ImportedCount = kpmEntries.Count;
                    return result;
                }
            }
        }

        // 6. Проверяем парсинг пар "Key: Value" или "login:password"
        string title = "Запись из буфера";
        string user = "";
        string pass = "";
        string url = "";
        string totpSecret = "";
        string notes = "";

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (line.StartsWith("otpauth://", StringComparison.OrdinalIgnoreCase) &&
                totpSvc.TryParseOtpAuthInput(line, out string parsedSecret, out string? iss, out string? acc))
            {
                totpSecret = parsedSecret;
                if (title == "Запись из буфера" && !string.IsNullOrWhiteSpace(iss)) title = iss;
                if (string.IsNullOrEmpty(user) && !string.IsNullOrWhiteSpace(acc)) user = acc;
                continue;
            }

            int colonIdx = line.IndexOf(':');
            if (colonIdx > 0 && colonIdx < line.Length - 1)
            {
                string key = line.Substring(0, colonIdx).Trim().ToLowerInvariant();
                string val = line.Substring(colonIdx + 1).Trim();

                if (key.Contains("login") || key.Contains("user") || key.Contains("логин") || key.Contains("почта") || key.Contains("email"))
                    user = val;
                else if (key.Contains("pass") || key.Contains("пароль") || key.Contains("secret") || key.Contains("ключ") || key.Contains("key"))
                    pass = val;
                else if (key.Contains("2fa") || key.Contains("totp") || key.Contains("otp"))
                    totpSecret = val;
                else if (key.Contains("url") || key.Contains("сайт") || key.Contains("ссылка") || key.Contains("http"))
                    url = val;
                else if (key.Contains("title") || key.Contains("название") || key.Contains("сервис") || key.Contains("программа"))
                    title = val;
                else if (line.IndexOf("://") < 0 && string.IsNullOrEmpty(user) && string.IsNullOrEmpty(pass) && !key.Contains(' '))
                {
                    user = line.Substring(0, colonIdx).Trim();
                    pass = val;
                }
                else
                    notes += (string.IsNullOrEmpty(notes) ? "" : "\n") + line;
            }
        }

        // Если в буфере просто одиночный пароль или токен (1 строка без пробелов)
        if (string.IsNullOrWhiteSpace(user) && string.IsNullOrWhiteSpace(pass) && lines.Length == 1 && !trimmed.Contains(' '))
        {
            if (totpSvc.ValidateSecret(trimmed) && trimmed.Length is >= 16 and <= 32)
            {
                totpSecret = trimmed.ToUpperInvariant();
                title = "Ключ 2FA (TOTP)";
            }
            else
            {
                pass = trimmed;
                title = trimmed.StartsWith("sk-") || trimmed.StartsWith("ghp_") || trimmed.StartsWith("eyJ")
                    ? "API Токен из буфера"
                    : "Ключ / Пароль из буфера";
            }
        }

        if (!string.IsNullOrWhiteSpace(user) || !string.IsNullOrWhiteSpace(pass) || !string.IsNullOrWhiteSpace(totpSecret))
        {
            if (title == "Запись из буфера" && !string.IsNullOrWhiteSpace(url))
                title = CleanUrlToTitle(url);

            result.ImportedEntries.Add(new KeyEntry
            {
                Title = title,
                UsernameOrEmail = user,
                SecretValue = pass,
                TotpSecret = totpSecret,
                TargetAppOrUrl = url,
                Notes = notes,
                Category = title.Contains("API") ? KeyCategory.ApiKey : KeyCategory.Account
            });
            result.ImportedCount++;
        }

        return result;
    }

    public async Task ExportToCsvAsync(IEnumerable<KeyEntry> entries, string filePath)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Title,Category,UsernameOrEmail,SecretValue,TotpSecret,TargetAppOrUrl,ExpirationDate,Notes");

        foreach (var e in entries)
        {
            string line = $"{EscapeCsv(e.Title)},{EscapeCsv(e.Category.ToString())},{EscapeCsv(e.UsernameOrEmail)},{EscapeCsv(e.SecretValue)},{EscapeCsv(e.TotpSecret)},{EscapeCsv(e.TargetAppOrUrl)},{EscapeCsv(e.ExpirationDate?.ToString("yyyy-MM-dd") ?? "")},{EscapeCsv(e.Notes)}";
            sb.AppendLine(line);
        }

        await File.WriteAllTextAsync(filePath, sb.ToString(), Encoding.UTF8);
    }

    public async Task ExportEmergencyRecoveryKitAsync(IEnumerable<KeyEntry> entries, string filePath)
    {
        var sb = new StringBuilder();
        sb.AppendLine("=========================================================================");
        sb.AppendLine("              STORM KEYS — АВАРИЙНЫЙ ЛИСТ ВОССТАНОВЛЕНИЯ                 ");
        sb.AppendLine("=========================================================================");
        sb.AppendLine($"Дата генерации (UTC): {DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"Устройство:           {Environment.MachineName}");
        sb.AppendLine("Шифрование контейнера: AES-256-GCM + Argon2id KDF");
        sb.AppendLine("ВНИМАНИЕ: Храните этот документ в сейфе или на защищенном офлайн-носителе!");
        sb.AppendLine("-------------------------------------------------------------------------");
        sb.AppendLine();

        int idx = 1;
        foreach (var e in entries)
        {
            sb.AppendLine($"[{idx++:D3}] {e.Title} ({e.CategoryDisplayName})");
            if (!string.IsNullOrWhiteSpace(e.UsernameOrEmail))
                sb.AppendLine($"      Логин / Email:  {e.UsernameOrEmail}");
            if (!string.IsNullOrWhiteSpace(e.SecretValue))
                sb.AppendLine($"      Пароль / Ключ:  {e.SecretValue}");
            if (!string.IsNullOrWhiteSpace(e.TotpSecret))
                sb.AppendLine($"      Секрет 2FA:     {e.TotpSecret}");
            if (!string.IsNullOrWhiteSpace(e.TargetAppOrUrl))
                sb.AppendLine($"      Сервис / URL:   {e.TargetAppOrUrl}");
            if (e.ExpirationDate.HasValue)
                sb.AppendLine($"      Срок действия:  {e.ExpirationDate.Value:yyyy-MM-dd}");
            if (e.Tags != null && e.Tags.Count > 0)
                sb.AppendLine($"      Теги:           {e.TagsDisplayText}");
            if (e.CustomFields != null && e.CustomFields.Count > 0)
            {
                foreach (var kv in e.CustomFields)
                    sb.AppendLine($"      Поле [{kv.Key}]: {kv.Value}");
            }
            if (!string.IsNullOrWhiteSpace(e.Notes))
                sb.AppendLine($"      Заметки:        {e.Notes.Replace("\r\n", " ").Replace("\n", " ")}");
            sb.AppendLine("-------------------------------------------------------------------------");
        }

        await File.WriteAllTextAsync(filePath, sb.ToString(), Encoding.UTF8);
    }

    public async Task ExportToJsonAsync(IEnumerable<KeyEntry> entries, string filePath)
    {
        var options = new JsonSerializerOptions { WriteIndented = true };
        string json = JsonSerializer.Serialize(entries, options);
        await File.WriteAllTextAsync(filePath, json, Encoding.UTF8);
    }

    public async Task ExportToDotEnvAsync(IEnumerable<KeyEntry> entries, string filePath)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# =========================================================================");
        sb.AppendLine($"# STORM KEYS — Экспорт переменных окружения и API-секретов (.env)");
        sb.AppendLine($"# Сгенерировано (UTC): {DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine("# =========================================================================");
        sb.AppendLine();

        var usedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var e in entries.Where(x => !x.IsDeleted && !string.IsNullOrWhiteSpace(x.SecretValue)))
        {
            string rawName = !string.IsNullOrWhiteSpace(e.Title) ? e.Title : e.UsernameOrEmail;
            string envKey = Regex.Replace(rawName.ToUpperInvariant(), @"[^A-Z0-9_]+", "_").Trim('_');
            if (string.IsNullOrWhiteSpace(envKey) || char.IsDigit(envKey[0]))
                envKey = "STORM_" + envKey;

            string baseKey = envKey;
            int suffix = 2;
            while (!usedKeys.Add(envKey))
            {
                envKey = $"{baseKey}_{suffix++}";
            }

            if (!string.IsNullOrWhiteSpace(e.TargetAppOrUrl) || !string.IsNullOrWhiteSpace(e.UsernameOrEmail))
            {
                sb.AppendLine($"# {e.Title} ({e.CategoryDisplayName}) {e.UsernameOrEmail} {e.TargetAppOrUrl}".Trim());
            }

            string escapedVal = e.SecretValue.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "").Replace("\n", "\\n");
            sb.AppendLine($"{envKey}=\"{escapedVal}\"");
        }

        await File.WriteAllTextAsync(filePath, sb.ToString(), Encoding.UTF8);
    }

    public async Task ExportEncryptedBackupAsync(IEnumerable<KeyEntry> entries, string filePath, string backupPassword)
    {
        if (string.IsNullOrWhiteSpace(backupPassword))
            throw new ArgumentException("Пароль резервного архива не может быть пустым.", nameof(backupPassword));

        var list = entries.Where(e => !e.IsDeleted).ToList();
        var container = new VaultContainer
        {
            Metadata = new VaultMetadata
            {
                AppName = "STORM KEYS BACKUP",
                SchemaVersion = "1.0.5",
                CreatedAt = DateTimeOffset.UtcNow,
                LastModifiedAt = DateTimeOffset.UtcNow,
                DeviceName = Environment.MachineName,
                EntryCount = list.Count
            },
            Entries = list
        };

        var crypto = new CryptoService();
        byte[] encrypted = await crypto.EncryptVaultAsync(container, backupPassword);
        await File.WriteAllBytesAsync(filePath, encrypted);
    }

    public async Task<ImportResult> ImportEncryptedBackupAsync(string filePath, string backupPassword)
    {
        var result = new ImportResult { FormatDetected = "Зашифрованный архив (.stormbackup)" };
        if (!File.Exists(filePath) || string.IsNullOrWhiteSpace(backupPassword))
            return result;

        var crypto = new CryptoService();
        byte[] encrypted = await File.ReadAllBytesAsync(filePath);
        var container = await crypto.DecryptVaultAsync(encrypted, backupPassword);
        if (container?.Entries != null)
        {
            foreach (var entry in container.Entries)
            {
                entry.Id = Guid.NewGuid();
                result.ImportedEntries.Add(entry);
                result.ImportedCount++;
            }
        }
        return result;
    }

    public async Task<ImportResult> ImportFromKeePassXmlAsync(string filePath)
    {
        var result = new ImportResult { FormatDetected = "KeePass 2.x XML (.xml)" };
        if (!File.Exists(filePath))
            return result;

        string xmlContent = await File.ReadAllTextAsync(filePath, Encoding.UTF8);
        var doc = XDocument.Parse(xmlContent);

        foreach (var entryElem in doc.Descendants().Where(x => x.Name.LocalName.Equals("Entry", StringComparison.OrdinalIgnoreCase)))
        {
            // Пропускаем записи истории внутри <History><Entry>
            if (entryElem.Parent != null && entryElem.Parent.Name.LocalName.Equals("History", StringComparison.OrdinalIgnoreCase))
                continue;

            string title = "";
            string user = "";
            string pass = "";
            string url = "";
            string notes = "";
            string totp = "";
            var custom = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var strElem in entryElem.Elements().Where(x => x.Name.LocalName.Equals("String", StringComparison.OrdinalIgnoreCase)))
            {
                string key = strElem.Elements().FirstOrDefault(x => x.Name.LocalName.Equals("Key", StringComparison.OrdinalIgnoreCase))?.Value?.Trim() ?? "";
                string val = strElem.Elements().FirstOrDefault(x => x.Name.LocalName.Equals("Value", StringComparison.OrdinalIgnoreCase))?.Value ?? "";
                if (string.IsNullOrEmpty(key)) continue;

                switch (key.ToLowerInvariant())
                {
                    case "title":
                        title = val;
                        break;
                    case "username":
                        user = val;
                        break;
                    case "password":
                        pass = val;
                        break;
                    case "url":
                        url = val;
                        break;
                    case "notes":
                        notes = val;
                        break;
                    case "otp":
                    case "totp":
                    case "timeotp-secret-base32":
                        totp = val;
                        break;
                    default:
                        if (!string.IsNullOrWhiteSpace(val))
                            custom[key] = val;
                        break;
                }
            }

            if (!string.IsNullOrWhiteSpace(title) || !string.IsNullOrWhiteSpace(user) || !string.IsNullOrWhiteSpace(pass))
            {
                result.ImportedEntries.Add(new KeyEntry
                {
                    Title = string.IsNullOrWhiteSpace(title) ? (string.IsNullOrWhiteSpace(url) ? "KeePass Запись" : CleanUrlToTitle(url)) : title,
                    UsernameOrEmail = user,
                    SecretValue = pass,
                    TargetAppOrUrl = url,
                    Notes = notes,
                    TotpSecret = totp,
                    CustomFields = custom,
                    Category = KeyCategory.Account,
                    CreatedAt = DateTimeOffset.UtcNow,
                    ModifiedAt = DateTimeOffset.UtcNow
                });
                result.ImportedCount++;
            }
        }

        return result;
    }

    private static string? GetPropString(JsonElement elem, params string[] propNames)
    {
        foreach (var p in propNames)
        {
            if (elem.TryGetProperty(p, out var val))
                return val.GetString();
        }
        return null;
    }

    public static string CreateSecureShareToken(KeyEntry entry, string passphrase = "STORM")
    {
        var clone = entry.Clone();
        clone.Title = entry.Title;
        clone.UsageCount = 0;
        clone.PasswordHistory = new();
        var jsonBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(clone));

        byte[] salt = RandomNumberGenerator.GetBytes(16);
        byte[] nonce = RandomNumberGenerator.GetBytes(12);
        byte[] tag = new byte[16];
        byte[] cipher = new byte[jsonBytes.Length];

        byte[] key = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(string.IsNullOrWhiteSpace(passphrase) ? "STORM" : passphrase),
            salt,
            100_000,
            HashAlgorithmName.SHA256,
            32);

        using (var aes = new AesGcm(key, 16))
        {
            aes.Encrypt(nonce, jsonBytes, cipher, tag);
        }

        byte[] payload = new byte[16 + 12 + 16 + cipher.Length];
        Buffer.BlockCopy(salt, 0, payload, 0, 16);
        Buffer.BlockCopy(nonce, 0, payload, 16, 12);
        Buffer.BlockCopy(tag, 0, payload, 28, 16);
        Buffer.BlockCopy(cipher, 0, payload, 44, cipher.Length);

        return "stormshare://" + Convert.ToBase64String(payload);
    }

    public static KeyEntry? TryDecodeSecureShareToken(string? tokenText, string passphrase = "STORM")
    {
        if (string.IsNullOrWhiteSpace(tokenText)) return null;
        var trimmed = tokenText.Trim();
        if (!trimmed.StartsWith("stormshare://", StringComparison.OrdinalIgnoreCase))
            return null;

        try
        {
            string b64 = trimmed["stormshare://".Length..].Trim();
            byte[] payload = Convert.FromBase64String(b64);
            if (payload.Length <= 44) return null;

            byte[] salt = payload[..16];
            byte[] nonce = payload[16..28];
            byte[] tag = payload[28..44];
            byte[] cipher = payload[44..];
            byte[] plain = new byte[cipher.Length];

            byte[] key = Rfc2898DeriveBytes.Pbkdf2(
                Encoding.UTF8.GetBytes(string.IsNullOrWhiteSpace(passphrase) ? "STORM" : passphrase),
                salt,
                100_000,
                HashAlgorithmName.SHA256,
                32);

            using (var aes = new AesGcm(key, 16))
            {
                aes.Decrypt(nonce, cipher, tag, plain);
            }

            var entry = JsonSerializer.Deserialize<KeyEntry>(Encoding.UTF8.GetString(plain));
            if (entry != null)
            {
                entry.Id = Guid.NewGuid();
                entry.ModifiedAt = DateTimeOffset.UtcNow;
            }
            return entry;
        }
        catch
        {
            return null;
        }
    }

    public async Task ExportToPrintableHtmlAsync(IEnumerable<KeyEntry> entries, string filePath)
    {
        var list = entries.ToList();
        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html lang=\"ru\"><head><meta charset=\"UTF-8\">");
        sb.AppendLine("<title>STORM KEYS — Резервный отчёт сейфа</title>");
        sb.AppendLine("<style>");
        sb.AppendLine("body{font-family:'Segoe UI',system-ui,sans-serif;background:#0B0E17;color:#F8FAFC;margin:24px;}");
        sb.AppendLine("h1{color:#00F0FF;letter-spacing:1.5px;margin-bottom:4px;}");
        sb.AppendLine(".meta{color:#94A3B8;font-size:13px;margin-bottom:20px;}");
        sb.AppendLine("table{width:100%;border-collapse:collapse;background:#131826;border-radius:10px;overflow:hidden;}");
        sb.AppendLine("th,td{padding:10px 12px;border-bottom:1px solid #1E293B;text-align:left;font-size:13px;word-break:break-all;}");
        sb.AppendLine("th{background:#1E293B;color:#38BDF8;font-weight:700;}");
        sb.AppendLine("code{font-family:Consolas,monospace;color:#A7F3D0;background:#0F172A;padding:2px 6px;border-radius:4px;}");
        sb.AppendLine("@media print{body{background:#fff;color:#000;}table{background:#fff;}th{background:#eee;color:#000;}code{background:#f3f4f6;color:#111;}}");
        sb.AppendLine("</style></head><body>");
        sb.AppendLine("<h1>STORM KEYS — РЕЗЕРВНЫЙ ПАСПОРТ СЕЙФА</h1>");
        sb.AppendLine($"<div class=\"meta\">Сформировано: {DateTime.Now:dd.MM.yyyy HH:mm} • Всего записей: {list.Count} • Храните в защищённом месте</div>");
        sb.AppendLine("<table><thead><tr><th>Сервис</th><th>Категория</th><th>Логин / ID</th><th>Пароль / Ключ</th><th>URL / Приложение</th><th>2FA / Теги</th></tr></thead><tbody>");
        foreach (var e in list)
        {
            string svc = System.Net.WebUtility.HtmlEncode(e.Title);
            string cat = System.Net.WebUtility.HtmlEncode(e.CategoryDisplayName);
            string usr = System.Net.WebUtility.HtmlEncode(e.UsernameOrEmail);
            string sec = System.Net.WebUtility.HtmlEncode(e.SecretValue);
            string url = System.Net.WebUtility.HtmlEncode(e.TargetAppOrUrl);
            string extra = System.Net.WebUtility.HtmlEncode($"{(e.HasTotp ? "2FA " : "")}{e.Tags}");
            sb.AppendLine($"<tr><td><b>{svc}</b></td><td>{cat}</td><td>{usr}</td><td><code>{sec}</code></td><td>{url}</td><td>{extra}</td></tr>");
        }
        sb.AppendLine("</tbody></table></body></html>");
        await File.WriteAllTextAsync(filePath, sb.ToString(), new UTF8Encoding(false));
    }

    private static string EscapeCsv(string text)
    {
        if (string.IsNullOrEmpty(text)) return "\"\"";
        if (text.Contains(',') || text.Contains('"') || text.Contains('\n') || text.Contains('\r'))
            return $"\"{text.Replace("\"", "\"\"")}\"";
        return text;
    }

    private static string CleanUrlToTitle(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return "Без названия";
        try
        {
            if (!url.StartsWith("http://") && !url.StartsWith("https://"))
                url = "https://" + url;
            var uri = new Uri(url);
            string host = uri.Host;
            if (host.StartsWith("www.")) host = host.Substring(4);
            return host;
        }
        catch
        {
            return url;
        }
    }

    private static List<string> ParseCsvRow(string line)
    {
        var result = new List<string>();
        bool inQuotes = false;
        var cur = new StringBuilder();

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (c == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    cur.Append('"');
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (c == ',' && !inQuotes)
            {
                result.Add(cur.ToString().Trim());
                cur.Clear();
            }
            else
            {
                cur.Append(c);
            }
        }
        result.Add(cur.ToString().Trim());
        return result;
    }
}
