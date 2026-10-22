using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using StormKeys.Models;

namespace StormKeys.Services;

public record PwnedCheckResult(string Password, int BreachCount, bool IsBreached, bool IsSuccess, string ErrorMessage = "");

public record PwnedAuditSummary(int TotalChecked, int BreachedCount, int CleanCount, int ErrorCount)
{
    public int BreachedEntriesCount => BreachedCount;
    public int CleanEntriesCount => CleanCount;
}

public interface IPwnedPasswordsService
{
    Task<PwnedCheckResult> CheckPasswordAsync(string password, CancellationToken ct = default);
    Task<PwnedAuditSummary> AuditEntriesAsync(IEnumerable<KeyEntry> entries, IProgress<double>? progress = null, CancellationToken ct = default);
    Task<PwnedAuditSummary> AuditVaultAsync(IEnumerable<KeyEntry> entries, IProgress<double>? progress = null, CancellationToken ct = default);
}

public sealed class PwnedPasswordsService : IPwnedPasswordsService
{
    private const string ApiBaseUrl = "https://api.pwnedpasswords.com/range/";
    private readonly HttpClient _httpClient;

    public PwnedPasswordsService(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient();
        if (!_httpClient.DefaultRequestHeaders.Contains("User-Agent"))
        {
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "STORM-KEYS-PasswordAudit/1.0");
        }
    }

    public async Task<PwnedCheckResult> CheckPasswordAsync(string password, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(password))
            return new PwnedCheckResult(password, 0, false, true);

        try
        {
            byte[] pwdBytes = Encoding.UTF8.GetBytes(password);
            byte[] hashBytes = SHA1.HashData(pwdBytes);
            string fullHash = Convert.ToHexString(hashBytes).ToUpperInvariant();

            string prefix = fullHash[..5];
            string suffix = fullHash[5..];

            string requestUrl = $"{ApiBaseUrl}{prefix}";
            using var request = new HttpRequestMessage(HttpMethod.Get, requestUrl);
            // k-Anonymity Add-Padding for enhanced privacy
            request.Headers.Add("Add-Padding", "true");

            using var response = await _httpClient.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                return new PwnedCheckResult(password, 0, false, false, $"API HTTP {(int)response.StatusCode}");
            }

            string content = await response.Content.ReadAsStringAsync(ct);
            using var reader = new StringReader(content);

            string? line;
            while ((line = await reader.ReadLineAsync(ct)) != null)
            {
                var parts = line.Split(':');
                if (parts.Length == 2)
                {
                    string returnedSuffix = parts[0].Trim().ToUpperInvariant();
                    if (string.Equals(returnedSuffix, suffix, StringComparison.OrdinalIgnoreCase))
                    {
                        if (int.TryParse(parts[1].Trim(), out int count))
                        {
                            return new PwnedCheckResult(password, count, count > 0, true);
                        }
                    }
                }
            }

            return new PwnedCheckResult(password, 0, false, true);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new PwnedCheckResult(password, 0, false, false, ex.Message);
        }
    }

    public async Task<PwnedAuditSummary> AuditEntriesAsync(
        IEnumerable<KeyEntry> entries,
        IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        var list = new List<KeyEntry>(entries);
        int total = list.Count;
        if (total == 0)
            return new PwnedAuditSummary(0, 0, 0, 0);

        int breached = 0;
        int clean = 0;
        int errors = 0;

        for (int i = 0; i < total; i++)
        {
            ct.ThrowIfCancellationRequested();
            var entry = list[i];

            if (string.IsNullOrEmpty(entry.SecretValue))
            {
                clean++;
                progress?.Report((double)(i + 1) / total * 100.0);
                continue;
            }

            var result = await CheckPasswordAsync(entry.SecretValue, ct);
            entry.PwnedLastCheckedAt = DateTimeOffset.UtcNow;

            if (result.IsSuccess)
            {
                entry.PwnedBreachCount = result.BreachCount;
                if (result.IsBreached)
                    breached++;
                else
                    clean++;
            }
            else
            {
                errors++;
            }

            progress?.Report((double)(i + 1) / total * 100.0);

            // Rate-limiting delay (HIBP recommends gentle pacing)
            await Task.Delay(100, ct);
        }

        return new PwnedAuditSummary(total, breached, clean, errors);
    }

    public Task<PwnedAuditSummary> AuditVaultAsync(IEnumerable<KeyEntry> entries, IProgress<double>? progress = null, CancellationToken ct = default)
        => AuditEntriesAsync(entries, progress, ct);
}
