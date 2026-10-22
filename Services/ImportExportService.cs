using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
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
    ImportResult ImportFromClipboardText(string text);
    Task ExportToCsvAsync(IEnumerable<KeyEntry> entries, string filePath);
    Task ExportToJsonAsync(IEnumerable<KeyEntry> entries, string filePath);
}

public class ImportExportService : IImportExportService
{
    public async Task<ImportResult> ImportFromCsvAsync(string filePath)
    {
        var result = new ImportResult();
        var lines = await File.ReadAllLinesAsync(filePath, Encoding.UTF8);

        if (lines.Length <= 1)
            return result;

        string headerLine = lines[0].Trim();
        var headers = ParseCsvRow(headerLine);

        // Маппинг колонок
        int nameIdx = -1, urlIdx = -1, userIdx = -1, passIdx = -1, noteIdx = -1;

        for (int i = 0; i < headers.Count; i++)
        {
            string h = headers[i].ToLowerInvariant().Trim();
            if (h.Contains("user") || h.Contains("login") || h.Contains("email")) 
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

        // Если не найдено имя, но есть URL
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

            if (string.IsNullOrWhiteSpace(title) && !string.IsNullOrWhiteSpace(url))
                title = CleanUrlToTitle(url);

            if (string.IsNullOrWhiteSpace(user) && string.IsNullOrWhiteSpace(pass))
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
                string uri = "";

                if (item.TryGetProperty("login", out var loginElem))
                {
                    if (loginElem.TryGetProperty("username", out var u)) username = u.GetString() ?? "";
                    if (loginElem.TryGetProperty("password", out var p)) password = p.GetString() ?? "";
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

                if (!string.IsNullOrWhiteSpace(user) || !string.IsNullOrWhiteSpace(secret))
                {
                    result.ImportedEntries.Add(new KeyEntry
                    {
                        Title = title,
                        UsernameOrEmail = user,
                        SecretValue = secret,
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

        string[] lines = text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries);

        // Проверяем паттерн "Ключ активации": XXXXX-XXXXX-XXXXX-XXXXX-XXXXX (25 символов)
        var licenseRegex = new Regex(@"([A-Z0-9]{4,5}-[A-Z0-9]{4,5}-[A-Z0-9]{4,5}-[A-Z0-9]{4,5}(-[A-Z0-9]{4,5})?)", RegexOptions.IgnoreCase);
        var match = licenseRegex.Match(text);
        if (match.Success)
        {
            result.ImportedEntries.Add(new KeyEntry
            {
                Title = "Лицензионный ключ из буфера",
                SecretValue = match.Value,
                Category = KeyCategory.License,
                Notes = text
            });
            result.ImportedCount++;
            return result;
        }

        // Проверяем парсинг пар "Key: Value"
        string title = "Запись из буфера";
        string user = "";
        string pass = "";
        string url = "";
        string notes = "";

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            int colonIdx = line.IndexOf(':');
            if (colonIdx > 0 && colonIdx < line.Length - 1)
            {
                string key = line.Substring(0, colonIdx).Trim().ToLowerInvariant();
                string val = line.Substring(colonIdx + 1).Trim();

                if (key.Contains("login") || key.Contains("user") || key.Contains("логин") || key.Contains("почта") || key.Contains("email"))
                    user = val;
                else if (key.Contains("pass") || key.Contains("пароль") || key.Contains("secret") || key.Contains("ключ") || key.Contains("key"))
                    pass = val;
                else if (key.Contains("url") || key.Contains("сайт") || key.Contains("ссылка") || key.Contains("http"))
                    url = val;
                else if (key.Contains("title") || key.Contains("название") || key.Contains("сервис") || key.Contains("программа"))
                    title = val;
                else
                    notes += (string.IsNullOrEmpty(notes) ? "" : "\n") + line;
            }
            else if (line.Contains(":") && line.IndexOf("://") < 0)
            {
                // Формат "user:password"
                var parts = line.Split(':', 2);
                if (parts.Length == 2 && string.IsNullOrEmpty(user) && string.IsNullOrEmpty(pass))
                {
                    user = parts[0].Trim();
                    pass = parts[1].Trim();
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(user) || !string.IsNullOrWhiteSpace(pass))
        {
            result.ImportedEntries.Add(new KeyEntry
            {
                Title = title,
                UsernameOrEmail = user,
                SecretValue = pass,
                TargetAppOrUrl = url,
                Notes = notes,
                Category = KeyCategory.Account
            });
            result.ImportedCount++;
        }

        return result;
    }

    public async Task ExportToCsvAsync(IEnumerable<KeyEntry> entries, string filePath)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Title,Category,UsernameOrEmail,SecretValue,TargetAppOrUrl,ExpirationDate,Notes");

        foreach (var e in entries)
        {
            string line = $"{EscapeCsv(e.Title)},{EscapeCsv(e.Category.ToString())},{EscapeCsv(e.UsernameOrEmail)},{EscapeCsv(e.SecretValue)},{EscapeCsv(e.TargetAppOrUrl)},{EscapeCsv(e.ExpirationDate?.ToString("yyyy-MM-dd") ?? "")},{EscapeCsv(e.Notes)}";
            sb.AppendLine(line);
        }

        await File.WriteAllTextAsync(filePath, sb.ToString(), Encoding.UTF8);
    }

    public async Task ExportToJsonAsync(IEnumerable<KeyEntry> entries, string filePath)
    {
        var options = new JsonSerializerOptions { WriteIndented = true };
        string json = JsonSerializer.Serialize(entries, options);
        await File.WriteAllTextAsync(filePath, json, Encoding.UTF8);
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
