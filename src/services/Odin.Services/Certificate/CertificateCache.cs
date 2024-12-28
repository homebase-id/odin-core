using System;
using System.Collections.Concurrent;
using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Odin.Core.Exceptions;

namespace Odin.Services.Certificate;
#nullable enable

// SEB:NOTE no async here to minimize overhead in happy path
// SEB:NOTE we accept interleaving threads in here. The end result is always the same.

public interface ICertificateCache
{
    X509Certificate2? LookupCertificate(string domain);
    X509Certificate2? LoadCertificate(string domain, string keyPemPath, string certificatePemPath);
    void RemoveCertificate(string certificatePemPath);
    void SaveToFile(string domain, string keyPem, string certificatePem, string keyPemPath, string certificatePemPath);
}

//

public class CertificateCache : ICertificateCache
{
    private readonly ConcurrentDictionary<string, X509Certificate2?> _cache = new ();
    private readonly object _fileMutex = new ();

    //
    
    public X509Certificate2? LookupCertificate(string domain)
    {
        _cache.TryGetValue(domain, out var x509);
        
        if (x509 == null)
        {
            return null;
        }
        
        // Expired?
        var now = DateTime.Now; // NO UTC HERE, ChatGPT!
        if (now >= x509.NotBefore && now <= x509.NotAfter)
        {
            return x509;
        }
        
        _cache.TryRemove(domain, out _);
        return null;
    }
    
    //
    
    public X509Certificate2? LoadCertificate(string domain, string keyPemPath, string certificatePemPath)
    {
        _cache.GetOrAdd(domain, _ => LoadFromFile(domain, keyPemPath, certificatePemPath));
        
        // Double look-up to take care of expiration 
        return LookupCertificate(domain);
    }
    
    //

    public void RemoveCertificate(string certificatePemPath)
    {
        var cacheKey = certificatePemPath.ToLower();
        _cache.TryRemove(cacheKey, out _);
    }
    
    //
    
    public void SaveToFile(string domain, string keyPem, string certificatePem, string keyPemPath, string certificatePemPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(keyPemPath) ?? "");
        Directory.CreateDirectory(Path.GetDirectoryName(certificatePemPath) ?? "");

        lock (_fileMutex)
        {
            File.WriteAllText(keyPemPath, keyPem);
            File.WriteAllText(certificatePemPath, certificatePem);
        }

        UpdateCertificate(domain, keyPemPath, certificatePemPath);
    }
    
    //

    private X509Certificate2? LoadFromFile(string domain, string keyPemPath, string certificatePemPath)
    {
        string certPem;
        string keyPem;
        lock (_fileMutex)
        {
            if (!File.Exists(certificatePemPath) || !File.Exists(keyPemPath))
            {
                return null;
            }

            certPem = File.ReadAllText(certificatePemPath);
            keyPem = File.ReadAllText(keyPemPath);
        }

        // SEB:TODO test this on Windows
        // https://github.com/Azure/azure-iot-sdk-csharp/issues/2150
        var x509 = X509Certificate2.CreateFromPem(certPem, keyPem);

        // Sanity check certificate
        ThrowIfBadCertificate(domain, x509);

        return x509;
    }

    //
    
    private void UpdateCertificate(string domain, string keyPemPath, string certificatePemPath)
    {
        var x509 = LoadFromFile(domain, keyPemPath, certificatePemPath);
        if (x509 == null)
        {
            _cache.TryRemove(domain, out _);
        }
        else
        {
            _cache[domain] = x509;
        }
    }
    
    //

    private static void ThrowIfBadCertificate(string domain, X509Certificate2 x509)
    {
        byte[] data = { 1, 2, 3, 4, 5 };
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