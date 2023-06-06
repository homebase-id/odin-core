using System;
using System.Collections.Concurrent;
using System.IO;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Serilog;

#nullable enable
namespace Youverse.Core.Services.Certificate;

// SEB:TODO dependency inject this class
// SEB:NOTE no async here to minimize overhead in happy path
// SEB:NOTE we accept interleaving threads in here. The end result is always the same.
public static class DotYouCertificateCache
{
    private static readonly ConcurrentDictionary<string, X509Certificate2?> Cache = new ();
    private static readonly object FileMutex = new ();
    
    public static X509Certificate2? LookupCertificate(string domain)
    {
        Cache.TryGetValue(domain, out var x509);
        
        if (x509 == null)
        {
            return null;
        }
        
        // Expired?
        var now = DateTime.Now;
        if (now >= x509.NotBefore && now <= x509.NotAfter)
        {
            return x509;
        }
        
        Cache.TryRemove(domain, out _);
        return null;
    }
    
    //
    
    public static X509Certificate2? LoadCertificate(string domain, string keyPemPath, string certificatePemPath)
    {
        Cache.GetOrAdd(domain, _ => LoadFromFile(keyPemPath, certificatePemPath));
        
        // Double look-up to take care of expiration 
        return LookupCertificate(domain);
    }
    
    //

    public static void RemoveCertificate(string certificatePemPath)
    {
        var cacheKey = certificatePemPath.ToLower();
        Cache.TryRemove(cacheKey, out _);
    }
    
    //
    
    public static void SaveToFile(string domain, string keyPem, string certificatePem, string keyPemPath, string certificatePemPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(keyPemPath) ?? "");
        Directory.CreateDirectory(Path.GetDirectoryName(certificatePemPath) ?? "");

        lock (FileMutex)
        {
            File.WriteAllText(keyPemPath, keyPem);
            File.WriteAllText(certificatePemPath, certificatePem);
        }

        UpdateCertificate(domain, keyPemPath, certificatePemPath);
    }
    
    //

    private static X509Certificate2? LoadFromFile(string keyPemPath, string certificatePemPath)
    {
        string certPem;
        string keyPem;
        lock (FileMutex)
        {
            if (!File.Exists(certificatePemPath) || !File.Exists(keyPemPath))
            {
                return null;
            }

            certPem = File.ReadAllText(certificatePemPath);
            keyPem = File.ReadAllText(keyPemPath);
        }

        // Work around for error "No credentials are available in the security package"
        // https://github.com/Azure/azure-iot-sdk-csharp/issues/2150
        using var temp = X509Certificate2.CreateFromPem(certPem, keyPem);
        var x509 = new X509Certificate2(temp.Export(X509ContentType.Pfx));
        
        return x509;
    }
    
    //
    
    private static void UpdateCertificate(string domain, string keyPemPath, string certificatePemPath)
    {
        var x509 = LoadFromFile(keyPemPath, certificatePemPath);
        if (x509 == null)
        {
            Cache.TryRemove(domain, out _);
        }
        else
        {
            Cache[domain] = x509; 
        }
    }
    
    //
    
}