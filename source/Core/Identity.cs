using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Lantoss.Core;

/// <summary>
/// Owns the local device certificate. Production uses the Windows key store; previews and transport tests use disposable in-memory identities.
/// </summary>
public sealed class Identity : IDisposable
{
    public X509Certificate2 Certificate
    {
        get;
    }
    public string Fingerprint
    {
        get;
    }
    private Identity(X509Certificate2 certificate)
    {
        Certificate = certificate;
        Fingerprint = KeyFingerprint(certificate);
    }
    public static Identity Ephemeral()
    {
        using RSA key = RSA.Create(3072);
        CertificateRequest request = new("CN=Lantoss local device", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
        OidCollection usages = [new Oid("1.3.6.1.5.5.7.3.1"), new Oid("1.3.6.1.5.5.7.3.2")];
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(usages, false));
        return new(request.CreateSelfSigned(DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow.AddYears(5)));
    }
    [SupportedOSPlatform("windows")]
    public static Identity Windows(MetadataStore store)
    {
        using X509Store certificates = new(StoreName.My, StoreLocation.CurrentUser);
        certificates.Open(OpenFlags.ReadWrite);
        if (!string.IsNullOrEmpty(store.Thumbprint))
        {
            X509Certificate2? existing = certificates.Certificates.Find(X509FindType.FindByThumbprint, store.Thumbprint, false)
                .FirstOrDefault(c => c.HasPrivateKey && c.NotAfter.ToUniversalTime() > DateTime.UtcNow.AddDays(1));
            if (existing == null)
                throw new InvalidDataException("This device's Windows certificate is missing or expired. Restore it or use a new data directory and pair the devices again.");
            return new(existing);
        }
        using Identity ephemeral = Ephemeral();
        byte[] pfx = ephemeral.Certificate.Export(X509ContentType.Pkcs12, "");
        try
        {
            X509Certificate2 persistent = X509CertificateLoader.LoadPkcs12(pfx, "", X509KeyStorageFlags.UserKeySet | X509KeyStorageFlags.PersistKeySet);
            persistent.FriendlyName = "Lantoss local device identity";
            certificates.Add(persistent);
            store.SetCertificate(persistent.Thumbprint);
            return new(persistent);
        }
        finally { CryptographicOperations.ZeroMemory(pfx); }
    }
    public static string KeyFingerprint(X509Certificate certificate)
    {
        using X509Certificate2 publicCertificate = X509CertificateLoader.LoadCertificate(certificate.GetRawCertData());
        return Convert.ToHexString(SHA256.HashData(publicCertificate.PublicKey.ExportSubjectPublicKeyInfo()));
    }
    public static bool Acceptable(X509Certificate? certificate)
    {
        if (certificate == null)
            return false;
        try
        {
            using X509Certificate2 cert = X509CertificateLoader.LoadCertificate(certificate.GetRawCertData());
            using RSA? rsa = cert.GetRSAPublicKey();
            return rsa is { KeySize: >= 2048 } && cert.NotBefore.ToUniversalTime() <= DateTime.UtcNow && cert.NotAfter.ToUniversalTime() > DateTime.UtcNow;
        }
        catch (CryptographicException) { return false; }
    }
    public static string Comparison(string first, string second, string nonce)
    {
        string[] keys = [first, second];
        Array.Sort(keys, StringComparer.Ordinal);
        // Compatibility-sensitive transcript label: changing the old product name here breaks comparison codes with 1.x peers.
        string hash = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("FlowShare pairing v1\n" + keys[0] + "\n" + keys[1] + "\n" + nonce)));
        return $"{hash[..4]} {hash[4..8]} {hash[8..12]}";
    }
    public void Dispose() => Certificate.Dispose();
}

public sealed class PairingCode
{
    private readonly object gate = new();
    private string code = ""; private DateTime expires;
    public PairingCode() => Rotate();
    public string Value
    {
        get
        {
            lock (gate)
            {
                if (DateTime.UtcNow >= expires)
                    Rotate();
                return code;
            }
        }
    }
    public DateTime ExpiresUtc
    {
        get
        {
            _ = Value;
            lock (gate)
                return expires;
        }
    }
    public void Rotate()
    {
        lock (gate)
        {
            const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
            // 32 equally likely symbols. No modulo bias.
            code = new string(RandomNumberGenerator.GetBytes(10).Select(b => alphabet[b & 31]).ToArray());
            code = code[..5] + "-" + code[5..];
            expires = DateTime.UtcNow.AddMinutes(5);
        }
    }
    public bool Consume(string submitted)
    {
        lock (gate)
        {
            string normalized = submitted.Replace("-", "").Replace(" ", "").ToUpperInvariant();
            byte[] a = System.Text.Encoding.ASCII.GetBytes(normalized), b = System.Text.Encoding.ASCII.GetBytes(code.Replace("-", ""));
            if (DateTime.UtcNow >= expires || !CryptographicOperations.FixedTimeEquals(a, b))
                return false;
            Rotate();
            return true;
        }
    }
}
