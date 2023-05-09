using System;
using System.Collections.Concurrent;
using System.IO;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Serilog;

#nullable enable
namespace Youverse.Core.Services.Certificate;

// SEB:TODO dependency injection
// SEB:NOTE no async here to minimize overhead in happy path
public static class DotYouCertificateCache
{
    private static readonly ConcurrentDictionary<string, X509Certificate2?> Cache = new ();
    private static readonly object FileMutex = new ();
    
    public static X509Certificate2? LoadCertificate(string keyPemPath, string certificatePemPath)
    {
        var cacheKey = certificatePemPath.ToLower();
        var x509 = Cache.GetOrAdd(cacheKey, _ => LoadFromFile(keyPemPath, certificatePemPath));
        
        // Expired?
        var now = DateTime.Now;
        if (x509 != null && (now < x509.NotBefore || now > x509.NotAfter))
        {
            x509 = null;
        }

        if (x509 == null)
        {
            Cache.TryRemove(cacheKey, out _);
        }

        return x509;
    }
    
    //

    public static void RemoveCertificate(string certificatePemPath)
    {
        var cacheKey = certificatePemPath.ToLower();
        Cache.TryRemove(cacheKey, out _);
    }
    
    //
    
    public static void SaveToFile(string keyPem, string certificatePem, string keyPemPath, string certificatePemPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(keyPemPath) ?? "");
        Directory.CreateDirectory(Path.GetDirectoryName(certificatePemPath) ?? "");

        lock (FileMutex)
        {
            File.WriteAllText(keyPemPath, keyPem);
            File.WriteAllText(certificatePemPath, certificatePem);
        }

        UpdateCertificate(keyPemPath, certificatePemPath);
    }
    
    //

    private static X509Certificate2? LoadFromFile(string keyPemPath, string certificatePemPath)
    {
        string certPem;
        string keyPem;
        lock (FileMutex)
        {
            if (!File.Exists(certificatePemPath) || !File.Exists(certificatePemPath))
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
    
    private static void UpdateCertificate(string keyPemPath, string certificatePemPath)
    {
        var cacheKey = certificatePemPath.ToLower();
        var x509 = LoadFromFile(keyPemPath, certificatePemPath);
        if (x509 == null)
        {
            Cache.TryRemove(cacheKey, out _);
        }
        else
        {
            Cache[cacheKey] = x509; 
        }
    }
    
    //
    
}