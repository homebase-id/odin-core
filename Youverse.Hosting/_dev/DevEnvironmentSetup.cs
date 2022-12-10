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
            if (youverseConfiguration.Development.PreconfiguredDomains.Any())
            {
                foreach (var domain in youverseConfiguration.Development.PreconfiguredDomains)
                {
                    certificates.Add(HashUtil.ReduceSHA256Hash(domain), domain);
                }
            }

            //demo server
            // certificates.Add(HashUtil.ReduceSHA256Hash("frodobaggins-me"), "frodobaggins.me");
            // certificates.Add(HashUtil.ReduceSHA256Hash("samwisegamgee-me"), "samwisegamgee.me");
            //

            //local development
            // certificates.Add(HashUtil.ReduceSHA256Hash("frodo-digital"), "frodo.digital");
            // certificates.Add(HashUtil.ReduceSHA256Hash("samwise-digital"), "samwise.digital");
            // certificates.Add(HashUtil.ReduceSHA256Hash("merry-onekin-io"), "merry.onekin.io");
            // certificates.Add(HashUtil.ReduceSHA256Hash("pippin-onekin-io"), "pippin.onekin.io");
            //
            // //app-development 
            // certificates.Add(HashUtil.ReduceSHA256Hash("legolas-onekin-io"), "legolas.onekin.io");
            // certificates.Add(HashUtil.ReduceSHA256Hash("gimli-onekin-io"), "gimli.onekin.io");
            // certificates.Add(HashUtil.ReduceSHA256Hash("aragorn-onekin-io"), "aragorn.onekin.io");

            foreach (var (id, domain) in certificates)
            {
                if (identityRegistry.Get(domain).GetAwaiter().GetResult() != null)
                {
                    continue;
                }

                var (sourcePublicKeyPath, sourcePrivateKeyPath) = GetSourceDomainPath(domain, youverseConfiguration);
                var registrationRequest = new IdentityRegistrationRequest()
                {
                    DotYouId = (DotYouIdentity)domain,
                    OptionalCertificatePemContent = new CertificatePemContent()
                    {
                        PublicKeyCertificate = File.ReadAllText(sourcePublicKeyPath),
                        PrivateKey = File.ReadAllText(sourcePrivateKeyPath)
                    }
                };

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

            var sourcePaths = GetSourceDomainPath(youverseConfiguration.Registry.ProvisioningDomain, youverseConfiguration);
            File.Copy(sourcePaths.publicKey, Path.Combine(targetPath, Path.GetFileName(sourcePaths.publicKey)), true);
            File.Copy(sourcePaths.privateKey, Path.Combine(targetPath, Path.GetFileName(sourcePaths.privateKey)), true);
        }

        private static (string publicKey, string privateKey) GetSourceDomainPath(string domain, YouverseConfiguration youverseConfiguration)
        {
            string root = Path.Combine(youverseConfiguration.Development!.SslSourcePath, domain);
            string sourcePublicKeyPath = Path.Combine(root, "certificate.crt");
            string sourcePrivateKeyPath = Path.Combine(root, "private.key");

            if (!File.Exists(sourcePublicKeyPath) || !File.Exists(sourcePrivateKeyPath))
            {
                throw new Exception($"Cannot find [{sourcePublicKeyPath}] or [{sourcePrivateKeyPath}]");
            }

            return (sourcePublicKeyPath, sourcePrivateKeyPath);
        }
    }
}