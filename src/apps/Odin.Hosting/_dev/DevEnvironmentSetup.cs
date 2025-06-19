using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Logging;
using Odin.Core;
using Odin.Core.Identity;
using Odin.Core.Tasks;
using Odin.Services.Certificate;
using Odin.Services.Configuration;
using Odin.Services.Registry;

namespace Odin.Hosting._dev
{
    public static class DevEnvironmentSetup
    {
        public static void RegisterPreconfiguredDomainsAsync(ILogger logger, OdinConfiguration odinConfiguration, IIdentityRegistry identityRegistry)
        {
            Dictionary<Guid, string> certificates = new();
            if (odinConfiguration.Development?.PreconfiguredDomains.Any() ?? false)
            {
                foreach (var domain in odinConfiguration.Development.PreconfiguredDomains)
                {
                    logger.LogInformation("Preconfigured domain added: {domain}", domain);
                    certificates.Add(ByteArrayUtil.ReduceSHA256Hash(domain), domain);
                }
            }

            foreach (var (id, domain) in certificates)
            {
                if (identityRegistry.GetAsync(domain).GetAwaiter().GetResult() != null)
                {
                    continue;
                }

                var registrationRequest = new IdentityRegistrationRequest()
                {
                    OdinId = (OdinId)domain,
                    PlanId = "dev-domain"
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
        /// <param name="logger"></param>
        /// <param name="odinConfiguration"></param>
        /// <param name="registry"></param>
        /// <param name="certificateStore"></param>
        public static void ConfigureIfPresent(ILogger logger, OdinConfiguration odinConfiguration, IIdentityRegistry registry, ICertificateStore certificateStore)
        {
            if (odinConfiguration.Development != null)
            {
                ConfigureSystemSsl(odinConfiguration, certificateStore);
                RegisterPreconfiguredDomainsAsync(logger, odinConfiguration, registry);
            }
        }

        private static void ConfigureSystemSsl(OdinConfiguration odinConfiguration, ICertificateStore certificateStore)
        {
            // Provisioning system
            if (odinConfiguration.Registry.ProvisioningEnabled)
            {
                try
                {
                    var sourcePaths = GetSourceDomainPath(odinConfiguration.Registry.ProvisioningDomain, odinConfiguration);
                    var privateKey = File.ReadAllText(sourcePaths.privateKey);
                    var publicKey = File.ReadAllText(sourcePaths.publicKey);
                    certificateStore.PutCertificateAsync(odinConfiguration.Registry.ProvisioningDomain, privateKey, publicKey).BlockingWait();
                }
                catch (Exception)
                {
                    // Swallow unless domain is running on 127.0.0.1
                    if (odinConfiguration.Registry.ProvisioningDomain.EndsWith("dotyou.cloud"))
                    {
                        throw;
                    }
                }
            }

            // Admin system
            if (odinConfiguration.Admin.ApiEnabled)
            {
                try
                {
                    var sourcePaths = GetSourceDomainPath(odinConfiguration.Admin.Domain, odinConfiguration);
                    var privateKey = File.ReadAllText(sourcePaths.privateKey);
                    var publicKey = File.ReadAllText(sourcePaths.publicKey);
                    certificateStore.PutCertificateAsync(odinConfiguration.Admin.Domain, privateKey, publicKey).BlockingWait();
                }
                catch (Exception)
                {
                    // Swallow unless domain is running on 127.0.0.1
                    if (odinConfiguration.Admin.Domain.EndsWith("dotyou.cloud"))
                    {
                        throw;
                    }
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