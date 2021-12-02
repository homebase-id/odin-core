using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Grpc.Net.Client;
using MagicOnion.Client;
using Youverse.Core;
using Youverse.Core.Services.Identity;
using Youverse.Core.Services.Registry;
using Youverse.Core.Trie;
using Youverse.Core.Util;

namespace Youverse.Hosting.IdentityRegistry
{
    public class IdentityRegistryRpc : IIdentityContextRegistry
    {
        private readonly Configuration _config;
        private readonly Trie<IdentityCertificate> _identityMap;
        private readonly List<string> _tempDomains = new();

        public IdentityRegistryRpc(Configuration config)
        {
            _config = config;
            _identityMap = new Trie<IdentityCertificate>();
            
        }

        public async void Initialize()
        {
            //HACK: this will not scale. it's only for prototrial
            //get all identity registrations from the registry server

            Console.WriteLine("Initializing IdentityRegistryRpc");
            var client = GetClient();

            var paging = new PageOptions(1, int.MaxValue);
            var page = await client.GetList(paging);

            Console.WriteLine($"Retrieved {page.Results.Count} Identities");

            foreach (var ident in page.Results)
            {
                Console.WriteLine($"Mapping {ident.DomainName} to cert");
                try
                {
                    this.CacheDomain(ident);
                }
                catch (Exception e)
                {
                    Console.WriteLine($"Failed to caching domain [{e.Message}]");
                }
            }
        }

        public IdentityCertificate ResolveCertificate(string domainName)
        {
            //Console.WriteLine($"Resolving certificate for [{domainName}]");
            var cert = _identityMap.LookupName(domainName) ?? LazyLoad(domainName).ConfigureAwait(false).GetAwaiter().GetResult();

            if (cert == null)
            {
                Console.WriteLine($"No cert found on registry server for [{domainName}]");
            }

            return cert;
        }

        public TenantStorageConfig ResolveStorageConfig(string domainName)
        {
            var path = PathUtil.Combine(_config.Host.TenantDataRootPath, domainName);
            var tempPath = PathUtil.Combine(_config.Host.TempTenantDataRootPath, domainName);
            var result = new TenantStorageConfig(PathUtil.Combine(path, "data"), PathUtil.Combine(tempPath, "temp"));
            return result;
        }

        public IEnumerable<string> GetDomains()
        {
            return _tempDomains;
        }

        private IdentityCertificate CacheDomain(IdentityRegistration ident)
        {
            IdentityCertificate identCert = Map(ident);
            _identityMap.AddDomain(ident.DomainName, identCert);
            _tempDomains.Add(ident.DomainName);

            return identCert;
        }
        
        private async Task<IdentityCertificate> LazyLoad(string domainName)
        {
            var cert = await GetClient().Get(domainName);
            if (cert == null)
            {
                Console.WriteLine($"No cert found on registry server for [{domainName}]");
                return null;
            }

            return CacheDomain(cert);
        }

        private IdentityCertificate Map(IdentityRegistration ident)
        {
            var location = new CertificateLocation()
            {
                CertificatePath = ident.PublicKeyCertificateRelativePath,
                PrivateKeyPath = ident.PrivateKeyRelativePath
            };

            var identCert = new IdentityCertificate(ident.DomainKey, ident.DomainName, null, location);
            return identCert;
        }
        
        
        private IRegistryRpcService GetClient()
        {
            var channel = GrpcChannel.ForAddress(_config.Host.RegistryServerUri);
            var client = MagicOnionClient.Create<IRegistryRpcService>(channel);
            return client;
        }
    }
}