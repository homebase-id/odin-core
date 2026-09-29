using System;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Odin.Core;
using Odin.Core.Cryptography.Crypto;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Core.Json;
using Odin.Core.Logging.CorrelationId;
using Odin.Core.Storage.Database.System.Table;
using Odin.Core.Storage.PubSub;
using Odin.Core.Time;
using Odin.Core.Util;

namespace Odin.Services.Certificate;
#nullable enable

// SEB:NOTE we accept interleaving threads in here. What keeps the end result the same is that
// a cache entry is only ever replaced by a certificate read at an equal or newer row version -
// see CacheUnlessNewerCached.

public interface ICertificateStore
{
    Task<X509Certificate2?> GetCertificateAsync(string domain);

    /// <summary>
    /// Reads the certificate from the database, ignoring this node's cache, and refreshes the
    /// cache with what it finds. See docs/certificate-issuance-locking.md.
    /// </summary>
    Task<X509Certificate2?> ReloadCertificateAsync(string domain);

    Task<X509Certificate2> PutCertificateAsync(string domain, string keyPem, string certificatePem);
    Task StoreFailedCertificateUpdateAsync(string domain, string errorText);
    void ClearCache();

    /// <summary>
    /// Starts listening for certificates written by other nodes. Call once, at startup.
    /// </summary>
    Task SubscribeToCertificateChangesAsync();
}

//

public class CertificateStore(
    IServiceProvider serviceProvider,
    CertificateStorageKey certificateStorageKey) : ICertificateStore
{
    private readonly byte[] _storageKey = certificateStorageKey.StorageKey;
    private readonly ConcurrentDictionary<string, CachedCertificate> _cache = new ();
    private readonly Guid _nodeId = Guid.NewGuid();
    private IPubSubSubscription? _certificateChangeSubscription;

    //

    public void ClearCache()
    {
        _cache.Clear();
    }

    //

    public async Task<X509Certificate2?> GetCertificateAsync(string domain)
    {
        var x509 = LookupAndValidateCertificate(domain);
        if (x509 != null)
        {
            return x509;
        }

        x509 = await ReloadCertificateAsync(domain);
        return x509;
    }

    //

    public async Task SubscribeToCertificateChangesAsync()
    {
        if (_certificateChangeSubscription != null)
        {
            return;
        }

        var pubSub = serviceProvider.GetRequiredService<ISystemPubSub>();
        _certificateChangeSubscription = await pubSub.SubscribeAsync(CertificateChangedMessage.Channel, OnCertificateChangedAsync);
    }

    //

    private async Task OnCertificateChangedAsync(JsonEnvelope envelope)
    {
        if (envelope.DeserializeMessage() is not CertificateChangedMessage message || message.OriginNodeId == _nodeId)
        {
            return;
        }

        // Refresh here rather than evict: an eviction would make the next TLS handshake pay for
        // the database read. The old certificate keeps being served until the new one is in.
        await ReloadCertificateAsync(message.Domain);
    }

    //

    private async Task PublishCertificateChangedAsync(string domain)
    {
        // Outside the try: a missing registration is a wiring bug, not a failed announcement
        var pubSub = serviceProvider.GetRequiredService<ISystemPubSub>();
        try
        {
            var envelope = JsonEnvelope.Create(new CertificateChangedMessage { Domain = domain, OriginNodeId = _nodeId });
            await pubSub.PublishAsync(CertificateChangedMessage.Channel, envelope);
        }
        catch (Exception e)
        {
            // The certificate is already committed, so the write must not fail over this
            var logger = serviceProvider.GetRequiredService<ILogger<CertificateStore>>();
            logger.LogWarning(e, "Could not announce the new certificate for {domain} to other nodes: {error}",
                domain, e.Message);
        }
    }

    //

    private X509Certificate2? LookupAndValidateCertificate(string domain)
    {
        _cache.TryGetValue(domain, out var cached);
        var x509 = cached?.Certificate;
        return IsValid(x509) ? x509 : null;
    }
    
    //

    public async Task<X509Certificate2?> ReloadCertificateAsync(string domain)
    {
        var odinId = new OdinId(domain);

        using var scope = serviceProvider.CreateScope();
        var tableCertificates = scope.ServiceProvider.GetRequiredService<TableCertificates>();

        var record = await tableCertificates.GetAsync(odinId);
        if (string.IsNullOrEmpty(record?.privateKey) || string.IsNullOrEmpty(record?.privateKey))
        {
            return null;
        }

        var iv = IvFromString(record.certificate);
        var decryptedKeyPem =
            AesCbc.Decrypt(Convert.FromHexString(record.privateKey), _storageKey, iv).ToStringFromUtf8Bytes();

        var x509 = X509FromPem(domain, decryptedKeyPem, record.certificate);
        if (IsValid(x509))
        {
            return CacheUnlessNewerCached(domain, x509, record.modified.milliseconds);
        }

        return null;
    }

    //

    // A cached certificate and the row version it was read at: Certificates.modified, which every
    // upsert moves strictly forward. Loads finish in any order - a TLS handshake's cache miss, a
    // change announcement from another node, the re-check after the order lock - and an
    // unconditional write let one that read the row early overwrite a newer certificate after
    // the fact, leaving this node serving the one it replaced.
    private sealed record CachedCertificate(X509Certificate2 Certificate, long Version);

    // internal for testing
    internal X509Certificate2 CacheUnlessNewerCached(string domain, X509Certificate2 x509, long version)
    {
        return _cache.AddOrUpdate(
            domain,
            _ => new CachedCertificate(x509, version),
            (_, cached) => cached.Version > version ? cached : new CachedCertificate(x509, version)).Certificate;
    }

    //

    public async Task<X509Certificate2> PutCertificateAsync(string domain, string keyPem, string certificatePem)
    {
        AsciiDomainNameValidator.AssertValidDomain(domain);
        ArgumentException.ThrowIfNullOrEmpty(keyPem, nameof(keyPem));
        ArgumentException.ThrowIfNullOrEmpty(certificatePem, nameof(certificatePem));

        var x509 = X509FromPem(domain, keyPem, certificatePem);
        if (!IsValid(x509))
        {
            throw new OdinSystemException($"Certificate for {domain} is not valid. Did it expire?");
        }

        var iv = IvFromString(certificatePem);
        var encryptedKeyPem = Convert.ToHexString(AesCbc.Encrypt(Encoding.UTF8.GetBytes(keyPem), _storageKey, iv));

        var correlationContext = serviceProvider.GetRequiredService<ICorrelationContext>();

        var odinId = new OdinId(domain);
        var record = new CertificatesRecord
        {
            domain = odinId,
            privateKey = encryptedKeyPem,
            certificate = certificatePem,
            expiration = UnixTimeUtc.FromDateTime(x509.NotAfter),
            lastAttempt = UnixTimeUtc.Now(),
            correlationId = correlationContext.Id,
            lastError = null
        };

        using var scope = serviceProvider.CreateScope();
        var tableCertificates = scope.ServiceProvider.GetRequiredService<TableCertificates>();
        await tableCertificates.UpsertAsync(record);
        CacheUnlessNewerCached(domain, x509, record.modified.milliseconds);

        await PublishCertificateChangedAsync(domain);

        return x509;
    }

    //

    public async Task StoreFailedCertificateUpdateAsync(string domain, string errorText)
    {
        var odinId = new OdinId(domain);
        var correlationContext = serviceProvider.GetRequiredService<ICorrelationContext>();

        using var scope = serviceProvider.CreateScope();
        var tableCertificates = scope.ServiceProvider.GetRequiredService<TableCertificates>();
        await tableCertificates.FailCertificateUpdate(odinId, UnixTimeUtc.Now(), correlationContext.Id, errorText);
    }

    //

    private static X509Certificate2 X509FromPem(string domain, string keyPem, string certificatePem)
    {
        var x509 = X509Certificate2.CreateFromPem(certificatePem, keyPem);
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // SEB:NOTE 29-Dec-2024 this is still required on Windows. WTH Microsoft??
            // https://github.com/Azure/azure-iot-sdk-csharp/issues/2150
            var pfxData = x509.Export(X509ContentType.Pfx);
            x509.Dispose();
            x509 = X509CertificateLoader.LoadPkcs12(pfxData, password: null);
        }

        // Sanity check certificate
        ThrowIfBadCertificate(domain, x509);

        return x509;
    }

    //

    private static bool IsValid(X509Certificate2? x509)
    {
        if (x509 == null)
        {
            return false;
        }
        var now = DateTime.Now; // NO UTC HERE, ChatGPT!
        return now >= x509.NotBefore && now <= x509.NotAfter;
    }
    
    //

    private static void ThrowIfBadCertificate(string domain, X509Certificate2 x509)
    {
        byte[] data = [1, 2, 3, 4, 5];
        byte[] signature;

        // Compute a signature using the private key
        using (var privateKey = x509.GetECDsaPrivateKey())
        {
            if (privateKey == null)
            {
                // SEB:NOTE if you get here all the time, double-check KeyAlgorithm when creating certificate.
                throw new OdinSystemException($"{domain}: no private key in x509 certificate. This should not happen!");
            }
            signature = privateKey.SignData(data, HashAlgorithmName.SHA256);
        }

        // Verify the signature using the public key
        using (var publicKey = x509.GetECDsaPublicKey())
        {
            if (publicKey == null)
            {
                // SEB:NOTE if you get here all the time, double-check KeyAlgorithm when creating certificate.
                throw new OdinSystemException($"{domain}: no public key in x509 certificate. This should not happen!");
            }

            if (!publicKey.VerifyData(data, signature, HashAlgorithmName.SHA256))
            {
                throw new OdinSystemException(
                    $"{domain}: the x509 private and public key do not work together. This should not happen!");
            }
        }
    }

    //

    private static byte[] IvFromString(string input)
    {
        ArgumentException.ThrowIfNullOrEmpty(input, nameof(input));

        var inputBytes = Encoding.UTF8.GetBytes(input);
        var hashBytes = SHA256.HashData(inputBytes);

        var iv = new byte[16];
        Array.Copy(hashBytes, 0, iv, 0, 16);

        return iv;
    }

    //

}

public class CertificateStorageKey(byte[] storageKey)
{
    public byte[] StorageKey { get; } = storageKey;
}
