using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Odin.Core;
using Odin.Core.Identity;
using Odin.Core.Services.Certificate;
using Odin.Core.Services.Configuration;
using Odin.Core.Services.Registry;
using Odin.Core.Util;
using Serilog;

namespace Odin.Hosting._dev
{
    public static class DevEnvironmentSetup
    {
        public static void RegisterPreconfiguredDomains(OdinConfiguration odinConfiguration, IIdentityRegistry identityRegistry)
        {
            Dictionary<Guid, string> certificates = new();
            if (odinConfiguration.Development?.PreconfiguredDomains.Any() ?? false)
            {
                foreach (var domain in odinConfiguration.Development.PreconfiguredDomains)
                {
                    Log.Information($"Preconfigured domain added:[]{domain}");
                    
                    certificates.Add(ByteArrayUtil.ReduceSHA256Hash(domain), domain);
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
                    var (sourcePublicKeyPath, sourcePrivateKeyPath) = GetSourceDomainPath(domain, odinConfiguration);
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
        /// <param name="odinConfiguration"></param>
        /// <param name="registry"></param>
        public static void ConfigureIfPresent(OdinConfiguration odinConfiguration, IIdentityRegistry registry)
        {
            if (odinConfiguration.Development != null)
            {
                ConfigureSystemSsl(odinConfiguration);
                RegisterPreconfiguredDomains(odinConfiguration, registry);
            }
        }

        private static void ConfigureSystemSsl(OdinConfiguration odinConfiguration)
        {
            string targetPath = Path.Combine(odinConfiguration.Host.SystemSslRootPath, odinConfiguration.Registry.ProvisioningDomain);
            Directory.CreateDirectory(targetPath);

            // Provisioning system
            try
            {
                var sourcePaths = GetSourceDomainPath(odinConfiguration.Registry.ProvisioningDomain, odinConfiguration);
                File.Copy(sourcePaths.publicKey, Path.Combine(targetPath, Path.GetFileName(sourcePaths.publicKey)), true);
                File.Copy(sourcePaths.privateKey, Path.Combine(targetPath, Path.GetFileName(sourcePaths.privateKey)), true);
            }
            catch (Exception)
            {
                // Swallow unless provisioning domain is running on 127.0.0.1
                if (odinConfiguration.Registry.ProvisioningDomain.EndsWith("dotyou.cloud"))
                {
                    throw;
                }
            }
        }

        private static (string publicKey, string privateKey) GetSourceDomainPath(string domain, OdinConfiguration odinConfiguration)
        {
            var root = Path.Combine(odinConfiguration.Development!.SslSourcePath, domain);

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