using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection.PortableExecutable;
using Youverse.Core.Identity;
using Youverse.Core.Services.Certificate;
using Youverse.Core.Services.Configuration;
using Youverse.Core.Services.Registry;
using Youverse.Core.Util;

namespace Youverse.Hosting._dev
{
    public static class DevEnvironmentSetup
    {
        public static void RegisterPreconfiguredDomains(YouverseConfiguration youverseConfiguration, IIdentityRegistry identityRegistry)
        {
            Dictionary<Guid, string> certificates = new();
            if (youverseConfiguration.Development?.PreconfiguredDomains.Any() ?? false)
            {
                foreach (var domain in youverseConfiguration.Development.PreconfiguredDomains)
                {
                    certificates.Add(HashUtil.ReduceSHA256Hash(domain), domain);
                }
            }

            foreach (var (id, domain) in certificates)
            {
                if (identityRegistry.Get(domain).GetAwaiter().GetResult() != null)
                {
                    // identityRegistry.DeleteRegistration(domain).GetAwaiter().GetResult();
                    continue;
                }

                var registrationRequest = new IdentityRegistrationRequest()
                {
                    OdinId = (OdinId)domain,
                };
                
                try
                {
                    var (sourcePublicKeyPath, sourcePrivateKeyPath) = GetSourceDomainPath(domain, youverseConfiguration);
                    registrationRequest.OptionalCertificatePemContent = new CertificatePemContent()
                    {
                        Certificate = File.ReadAllText(sourcePublicKeyPath),
                        PrivateKey = File.ReadAllText(sourcePrivateKeyPath)
                    };
                }
                catch (Exception)
                {
                    // Swallow unless identity domain is running on 127.0.0.1
                    if (domain.EndsWith("dotyou.cloud"))
                    {
                        throw;
                    }
                }

                identityRegistry.AddRegistration(registrationRequest).GetAwaiter().GetResult();
            }
        }

        /// <summary>
        /// Sets up development or demo environment
        /// </summary>
        /// <param name="youverseConfiguration"></param>
        /// <param name="registry"></param>
        public static void ConfigureIfPresent(YouverseConfiguration youverseConfiguration, IIdentityRegistry registry)
        {
            if (youverseConfiguration.Development != null)
            {
                ConfigureSystemSsl(youverseConfiguration);
                RegisterPreconfiguredDomains(youverseConfiguration, registry);
            }
        }

        private static void ConfigureSystemSsl(YouverseConfiguration youverseConfiguration)
        {
            string targetPath = Path.Combine(youverseConfiguration.Host.SystemSslRootPath, youverseConfiguration.Registry.ProvisioningDomain);
            Directory.CreateDirectory(targetPath);

            // Provisioning system
            try
            {
                var sourcePaths = GetSourceDomainPath(youverseConfiguration.Registry.ProvisioningDomain, youverseConfiguration);
                File.Copy(sourcePaths.publicKey, Path.Combine(targetPath, Path.GetFileName(sourcePaths.publicKey)), true);
                File.Copy(sourcePaths.privateKey, Path.Combine(targetPath, Path.GetFileName(sourcePaths.privateKey)), true);
            }
            catch (Exception)
            {
                // Swallow unless provisioning domain is running on 127.0.0.1
                if (youverseConfiguration.Registry.ProvisioningDomain.EndsWith("dotyou.cloud"))
                {
                    throw;
                }
            }
        }

        private static (string publicKey, string privateKey) GetSourceDomainPath(string domain, YouverseConfiguration youverseConfiguration)
        {
            var root = Path.Combine(youverseConfiguration.Development!.SslSourcePath, domain);
            
            var sourcePublicKeyPath = Path.Combine(root, "certificate.crt");
            if (!File.Exists(sourcePublicKeyPath))
            {
                throw new Exception($"Cannot find [{sourcePublicKeyPath}]");
            }
                
            var sourcePrivateKeyPath = Path.Combine(root, "private.key");
            if (!File.Exists(sourcePrivateKeyPath))
            {
                throw new Exception($"Cannot find [{sourcePrivateKeyPath}]");
            }

            return (sourcePublicKeyPath, sourcePrivateKeyPath);
        }
    }
}