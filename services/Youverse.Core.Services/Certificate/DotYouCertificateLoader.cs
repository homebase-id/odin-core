using System;
using System.Collections.Concurrent;
using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using Serilog;
using Youverse.Core.Exceptions;

namespace Youverse.Core.Services.Certificate;

public static class DotYouCertificateLoader
{
    private static readonly ConcurrentDictionary<string, X509Certificate2> _tempCache =
        new ConcurrentDictionary<string, X509Certificate2>(StringComparer.InvariantCultureIgnoreCase);

    // private static SemaphoreSlim _cacheLock = new (1, 1);
    private static readonly object _cacheLock = new();

    public static X509Certificate2 LoadCertificate(string publicKeyPath, string privateKeyPath,
        bool failIfInvalid = false)
    {
        // if (File.Exists(publicKeyPath) == false || File.Exists(privateKeyPath) == false)
        // {
        //     if(failIfInvalid)
        //     {
        //         throw new YouverseSystemException("Cannot find certificate or key file(s)");
        //     }
        //
        //     return null;
        // }

        string key = publicKeyPath.ToLower();
        return _tempCache.GetOrAdd(key, (k => LoadFromDisk(publicKeyPath, privateKeyPath)));

        // var cert = LoadFromCache(key);
        // if (cert != null)
        // {
        //     return cert;
        // }
        //
        // Log.Debug($"Cert Loader - from disk: {publicKeyPath}");
        // return LoadFromDisk(publicKeyPath, privateKeyPath);
    }

    public static X509Certificate2 LoadFromDisk(string publicKeyPath, string privateKeyPath)
    {
        using (X509Certificate2 publicKey = new X509Certificate2(publicKeyPath))
        {
            string encodedKey = File.ReadAllText(privateKeyPath);
            RSA rsaPrivateKey;
            using (rsaPrivateKey = RSA.Create())
            {
                rsaPrivateKey.ImportFromPem(encodedKey.ToCharArray());

                if (Environment.OSVersion.Platform == PlatformID.Win32NT)
                {
                    using (X509Certificate2 pubPrivEphemeral = publicKey.CopyWithPrivateKey(rsaPrivateKey))
                    {
                        // Export as PFX and re-import if you want "normal PFX private key lifetime"
                        // (this step is currently required for SslStream, but not for most other things
                        // using certificates)
                        return new X509Certificate2(pubPrivEphemeral.Export(X509ContentType.Pfx));
                    }
                }
                else
                {
                    return publicKey.CopyWithPrivateKey(rsaPrivateKey);
                }

                //// Disabled this part as it causes too many changes within Keychain causing Chrome to not open the Page:
                // using (X509Certificate2 pubPrivEphemeral = publicKey.CopyWithPrivateKey(rsaPrivateKey))
                // {
                //     // Export as PFX and re-import if you want "normal PFX private key lifetime"
                //     // (this step is currently required for SslStream, but not for most other things
                //     // using certificates)
                //     return new X509Certificate2(pubPrivEphemeral.Export(X509ContentType.Pfx));
                // }
            }
        }
    }

    public static X509Certificate2 LoadFromCache(string key)
    {
        if (_tempCache.TryGetValue(key, out var cachedCert))
        {
            Log.Debug($"Cert Loader - from cache");
            return cachedCert;
        }

        return null;
    }
}