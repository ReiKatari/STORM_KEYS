using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace StormKeys.Services;

public record UpdateInfo(string Version, string Changelog, string DownloadUrl, string FileName);

public interface IUpdateService
{
    Task<UpdateInfo?> CheckForUpdatesAsync(bool silent = true);
    Task<bool> DownloadAndInstallAsync(UpdateInfo info, IProgress<double>? progress = null, CancellationToken ct = default);
}

public sealed class UpdateService : IUpdateService
{
    private const string GitHubApiUrl = "https://api.github.com/repos/ReiKatari/STORM_KEYS/releases/latest";

    /// <summary>
    /// Динамическое определение текущей версии приложения из метаданных скомпилированной сборки (Single Source of Truth).
    /// Нормализуется к формату Major.Minor.Build (3 компонента), чтобы исключить ложные срабатывания при сравнении 1.0.4.0 и 1.0.4.
    /// </summary>
    public static Version CurrentVersion => NormalizeVersion(
        typeof(UpdateService).Assembly.GetName().Version
        ?? Assembly.GetEntryAssembly()?.GetName().Version
        ?? new Version(1, 0, 4));

    public static string CurrentVersionString => $"{CurrentVersion.Major}.{CurrentVersion.Minor}.{CurrentVersion.Build}";

    public static Version NormalizeVersion(Version v)
    {
        int major = Math.Max(0, v.Major);
        int minor = Math.Max(0, v.Minor);
        int build = Math.Max(0, v.Build);
        return new Version(major, minor, build);
    }

    public static bool IsNewerVersion(string? remoteTag, Version? current = null)
    {
        if (string.IsNullOrWhiteSpace(remoteTag))
            return false;

        string clean = remoteTag.Trim().TrimStart('v', 'V').Trim();
        int dashIdx = clean.IndexOf('-');
        if (dashIdx > 0)
            clean = clean[..dashIdx];

        if (!Version.TryParse(clean, out var parsedRemote))
            return false;

        var normRemote = NormalizeVersion(parsedRemote);
        var normCurrent = NormalizeVersion(current ?? CurrentVersion);

        // Обновление доступно ТОЛЬКО если версия на сервере СТРОГО больше текущей установленной
        return normRemote > normCurrent;
    }

    private readonly HttpClient _httpClient;

    public UpdateService()
    {
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(6)
        };
        _httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("STORM-KEYS", CurrentVersionString));
    }

    public async Task<UpdateInfo?> CheckForUpdatesAsync(bool silent = true)
    {
        try
        {
            using var response = await _httpClient.GetAsync(GitHubApiUrl).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return null;

            var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("tag_name", out var tagElem))
                return null;

            string tag = tagElem.GetString() ?? string.Empty;
            string cleanVersion = tag.TrimStart('v', 'V').Trim();

            if (!IsNewerVersion(cleanVersion, CurrentVersion))
                return null;

            string changelog = string.Empty;
            if (root.TryGetProperty("body", out var bodyElem))
                changelog = bodyElem.GetString() ?? string.Empty;

            string downloadUrl = string.Empty;
            string fileName = $"STORM_KEYS_{cleanVersion}_Setup.exe";

            if (root.TryGetProperty("assets", out var assetsElem) && assetsElem.ValueKind == JsonValueKind.Array)
            {
                foreach (var asset in assetsElem.EnumerateArray())
                {
                    if (asset.TryGetProperty("name", out var nameElem))
                    {
                        string aName = nameElem.GetString() ?? string.Empty;
                        if (aName.EndsWith("_Setup.exe", StringComparison.OrdinalIgnoreCase) || aName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                        {
                            if (asset.TryGetProperty("browser_download_url", out var urlElem))
                            {
                                downloadUrl = urlElem.GetString() ?? string.Empty;
                                fileName = aName;
                                break;
                            }
                        }
                    }
                }
            }

            if (string.IsNullOrEmpty(downloadUrl))
                return null;

            return new UpdateInfo(cleanVersion, changelog, downloadUrl, fileName);
        }
        catch
        {
            if (!silent) throw;
            return null;
        }
    }

    public async Task<bool> DownloadAndInstallAsync(UpdateInfo info, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        try
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "STORM_KEYS_Update");
            Directory.CreateDirectory(tempDir);
            string targetPath = Path.Combine(tempDir, info.FileName);

            using var response = await _httpClient.GetAsync(info.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? -1L;
            await using var contentStream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            await using var fileStream = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);

            var buffer = new byte[81920];
            long totalRead = 0;
            int bytesRead;

            while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length, ct).ConfigureAwait(false)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct).ConfigureAwait(false);
                totalRead += bytesRead;
                if (totalBytes > 0 && progress != null)
                {
                    progress.Report((double)totalRead / totalBytes * 100.0);
                }
            }

            fileStream.Close();

            // Автоматический запуск скачанного инсталлятора с правами администратора
            Process.Start(new ProcessStartInfo
            {
                FileName = targetPath,
                UseShellExecute = true
            });

            // Завершение текущего процесса для обновления
            Environment.Exit(0);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
