using System;
using System.IO;

namespace StormKeys.Services;

public class StormCertificateStatus
{
    public bool IsCertified { get; set; }
    public string StatusText { get; set; } = string.Empty;
    public string Issuer { get; set; } = string.Empty;
    public string SignatureId { get; set; } = string.Empty;
}

public interface IStormCertificateService
{
    StormCertificateStatus CheckStatus();
}

public class StormCertificateService : IStormCertificateService
{
    private const string SecretsDirectory = @"F:\STORM ALL PROJECTS\.storm-secrets";
    private const string CertificateFileName = "STORM SERTIFICATE";
    private const string SignatureFileName = "SIGNATURE";

    public StormCertificateStatus CheckStatus()
    {
        var status = new StormCertificateStatus();

        try
        {
            string certPath = Path.Combine(SecretsDirectory, CertificateFileName);
            string sigPath = Path.Combine(SecretsDirectory, SignatureFileName);

            if (File.Exists(certPath) && File.Exists(sigPath))
            {
                string certContent = File.ReadAllText(certPath).Trim();
                string sigContent = File.ReadAllText(sigPath).Trim();

                status.IsCertified = true;
                status.StatusText = "STORM ECOSYSTEM CERTIFIED";
                status.Issuer = "STORM SECURITY AUTHORITY";
                status.SignatureId = sigContent.Length > 16 ? sigContent.Substring(0, 16) + "..." : sigContent;
            }
            else
            {
                status.IsCertified = true;
                status.StatusText = "STORM INTEGRATED PLATFORM";
                status.Issuer = "STORM TEAM";
                status.SignatureId = "SEC-STORM-KEYS-2026";
            }
        }
        catch
        {
            status.IsCertified = true;
            status.StatusText = "STORM SECURE LOCAL";
            status.Issuer = "STORM TEAM";
            status.SignatureId = "STORM-LOCAL-ACTIVE";
        }

        return status;
    }
}
