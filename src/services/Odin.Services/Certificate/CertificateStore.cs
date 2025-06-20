using System;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Core.Logging.CorrelationId;
using Odin.Core.Storage.Database.System.Table;
using Odin.Core.Time;

namespace Odin.Services.Certificate;
#nullable enable

// SEB:NOTE we accept interleaving threads in here. The end result is always the same.

public interface ICertificateStore
{
    Task<X509Certificate2?> GetCertificateAsync(string domain);
    Task<X509Certificate2> PutCertificateAsync(string domain, string keyPem, string certificatePem);
    Task StoreFailedCertificateUpdateAsync(string domain, string errorText);
}

//

public class CertificateStore(IServiceProvider serviceProvider) : ICertificateStore
{
    private readonly ConcurrentDictionary<string, X509Certificate2> _cache = new ();

    //

    public async Task<X509Certificate2?> GetCertificateAsync(string domain)
    {
        var x509 = LookupValidCertificate(domain);
        if (x509 != null)
        {
            return x509;
        }

        x509 = await LoadValidCertificateAsync(domain);
        return x509;
    }

    //

    private X509Certificate2? LookupValidCertificate(string domain)
    {
        _cache.TryGetValue(domain, out var x509);
        return IsValid(x509) ? x509 : null;
    }
    
    //

    private async Task<X509Certificate2?> LoadValidCertificateAsync(string domain)
    {
        var odinId = new OdinId(domain);

        using var scope = serviceProvider.CreateScope();
        var tableCertificates = scope.ServiceProvider.GetRequiredService<TableCertificates>();

        var record = await tableCertificates.GetAsync(odinId);
        if (record?.privateKey == null || record?.certificate == null)
        {
            return null;
        }

        var x509 = X509FromPem(domain, record.privateKey, record.certificate);
        if (IsValid(x509))
        {
            _cache[domain] = x509;
            return x509;
        }

        return null;
    }

    //

    public async Task<X509Certificate2> PutCertificateAsync(string domain, string keyPem, string certificatePem)
    {
        var x509 = X509FromPem(domain, keyPem, certificatePem);
        if (!IsValid(x509))
        {
            throw new OdinSystemException($"Certificate for {domain} is not valid. Did it expire?");
        }

        _cache[domain] = x509;

        var correlationContext = serviceProvider.GetRequiredService<ICorrelationContext>();

        var odinId = new OdinId(domain);
        var record = new CertificatesRecord
        {
            domain = odinId,
            privateKey = keyPem,
            certificate = certificatePem,
            expiration = UnixTimeUtc.FromDateTime(x509.NotAfter),
            lastAttempt = UnixTimeUtc.Now(),
            correlationId = correlationContext.Id,
            lastError = null
        };

        using var scope = serviceProvider.CreateScope();
        var tableCertificates = scope.ServiceProvider.GetRequiredService<TableCertificates>();
        await tableCertificates.UpsertAsync(record);

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

}