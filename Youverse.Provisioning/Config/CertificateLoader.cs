using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Serilog;

namespace Youverse.Provisioning.Config;

public class CertificateLoader
{
    private static readonly ConcurrentDictionary<string, X509Certificate2> _tempCache = new ConcurrentDictionary<string, X509Certificate2>(StringComparer.InvariantCultureIgnoreCase);

    public static X509Certificate2 LoadCertificate(string publicKeyPath, string privateKeyPath, bool failIfInvalid = false)
    {
        if (File.Exists(publicKeyPath) == false || File.Exists(privateKeyPath) == false)
        {
            if (failIfInvalid)
            {
                throw new SystemException("Cannot find certificate or key file(s)");
            }

            return null;
        }

        string key = publicKeyPath.ToLower();
        if (_tempCache.TryGetValue(key, out var cachedCert))
        {
            Log.Debug($"Cert Loader - from cache: {publicKeyPath}");
            return cachedCert;
        }

        Log.Debug($"Cert Loader - from disk: {publicKeyPath}");

        X509Certificate2 certificate;
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
                        certificate = new X509Certificate2(pubPrivEphemeral.Export(X509ContentType.Pfx));
                    }
                }
                else
                {
                    certificate = publicKey.CopyWithPrivateKey(rsaPrivateKey);
                }

                _tempCache.TryAdd(key, certificate);
                return certificate;
            }
        }
    }
}