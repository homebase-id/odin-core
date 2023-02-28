using System;
using System.Collections.Concurrent;
using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Serilog;
using Youverse.Core.Cryptography;
using Youverse.Core.Exceptions;
using Youverse.Core.Identity;
using Youverse.Core.Services.Base;
using Youverse.Core.Util;

namespace Youverse.Core.Services.Registry
{
    public static class TestCertificateMemoryCache
    {
        private static readonly ConcurrentDictionary<string, X509Certificate2> _cache;
        private static object _lock = new();

        static TestCertificateMemoryCache()
        {
            _cache = new ConcurrentDictionary<string, X509Certificate2>(1, 1000, StringComparer.InvariantCultureIgnoreCase);
        }

        public static void AddCertificate(string domain, X509Certificate2 certificate)
        {
            lock (_lock)
            {
                _cache.TryAdd(domain, certificate);
            }
        }

        public static bool TryGetCertificate(string domain, out X509Certificate2 certificate)
        {
            return _cache.TryGetValue(domain, out certificate);
        }
    }

    public class CertificateResolver : ICertificateResolver
    {
        private readonly TenantContext _tenantContext;

        public CertificateResolver(TenantContext tenantContext)
        {
            _tenantContext = tenantContext;
        }

        public CertificateLocation GetSigningCertificate()
        {
            throw new NotImplementedException();
        }

        public X509Certificate2 GetSslCertificate()
        {
            Guid domainId = CalculateDomainId(_tenantContext.HostOdinId);

            if (!TestCertificateMemoryCache.TryGetCertificate(_tenantContext.HostOdinId, out var certificate))
            {
                string certificatePath = Path.Combine(_tenantContext.DataRoot, "ssl", domainId.ToString(), "certificate.crt");
                string privateKeyPath = Path.Combine(_tenantContext.DataRoot, "ssl", domainId.ToString(), "private.key");
                return LoadCertificate(_tenantContext.HostOdinId, certificatePath, privateKeyPath);
            }

            return certificate;
        }
        

        /// <summary>
        /// Loads and returns a certificate for the given odinId
        /// </summary>
        /// <param name="rootPath"></param>
        /// <param name="registryId"></param>
        /// <param name="odinId"></param>
        /// <returns></returns>
        public static X509Certificate2 GetSslCertificate(string rootPath, Guid registryId, OdinId odinId)
        {
            if (!TestCertificateMemoryCache.TryGetCertificate(odinId, out var certificate))
            {
                Guid domainId = CalculateDomainId(odinId);
                string certificatePath = PathUtil.Combine(rootPath, registryId.ToString(), "ssl", domainId.ToString(), "certificate.crt");
                string privateKeyPath = PathUtil.Combine(rootPath, registryId.ToString(), "ssl", domainId.ToString(), "private.key");
                return LoadCertificate(odinId, certificatePath, privateKeyPath);
            }
            return certificate;
        }

        public static X509Certificate2 LoadCertificate(string domain, string publicKeyPath, string privateKeyPath)
        {
            Log.Logger.Information($"Loading Certificate for {domain}");

            using (X509Certificate2 publicKey = new X509Certificate2(publicKeyPath))
            {
                string encodedKey = File.ReadAllText(privateKeyPath);
                RSA rsaPrivateKey;
                using (rsaPrivateKey = RSA.Create())
                {
                    rsaPrivateKey.ImportFromPem(encodedKey.ToCharArray());

                    X509Certificate2 certificate;
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

                    //// Disabled this part as it causes too many changes within Keychain causing Chrome to not open the Page:
                    // using (X509Certificate2 pubPrivEphemeral = publicKey.CopyWithPrivateKey(rsaPrivateKey))
                    // {
                    //     // Export as PFX and re-import if you want "normal PFX private key lifetime"
                    //     // (this step is currently required for SslStream, but not for most other things
                    //     // using certificates)
                    //     return new X509Certificate2(pubPrivEphemeral.Export(X509ContentType.Pfx));
                    // }
                    TestCertificateMemoryCache.AddCertificate(domain, certificate);
                    return certificate;
                }
            }
        }
        
        // public static X509Certificate2 LoadCertificate(string publicKeyPath, string privateKeyPath)
        // {
        //     if (File.Exists(publicKeyPath) == false || File.Exists(privateKeyPath) == false)
        //     {
        //         throw new YouverseSystemException("Cannot find certificate or key file(s)");
        //     }
        //
        //     using (X509Certificate2 publicKey = new X509Certificate2(publicKeyPath))
        //     {
        //         string encodedKey = File.ReadAllText(privateKeyPath);
        //         RSA rsaPrivateKey;
        //         using (rsaPrivateKey = RSA.Create())
        //         {
        //             rsaPrivateKey.ImportFromPem(encodedKey.ToCharArray());
        //
        //
        //             if (Environment.OSVersion.Platform == PlatformID.Win32NT)
        //             {
        //                 using (X509Certificate2 pubPrivEphemeral = publicKey.CopyWithPrivateKey(rsaPrivateKey))
        //                 {
        //                     // Export as PFX and re-import if you want "normal PFX private key lifetime"
        //                     // (this step is currently required for SslStream, but not for most other things
        //                     // using certificates)
        //                     return new X509Certificate2(pubPrivEphemeral.Export(X509ContentType.Pfx));
        //                 }
        //             }
        //             else
        //             {
        //                 return publicKey.CopyWithPrivateKey(rsaPrivateKey);
        //             }
        //
        //             //// Disabled this part as it causes too many changes within Keychain causing Chrome to not open the Page:
        //             // using (X509Certificate2 pubPrivEphemeral = publicKey.CopyWithPrivateKey(rsaPrivateKey))
        //             // {
        //             //     // Export as PFX and re-import if you want "normal PFX private key lifetime"
        //             //     // (this step is currently required for SslStream, but not for most other things
        //             //     // using certificates)
        //             //     return new X509Certificate2(pubPrivEphemeral.Export(X509ContentType.Pfx));
        //             // }
        //         }
        //     }
        // }

        public static Guid CalculateDomainId(OdinId input)
        {
            return HashUtil.ReduceSHA256Hash(input);
        }
    }
}