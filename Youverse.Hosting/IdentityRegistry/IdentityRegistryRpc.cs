using System;
using System.IO;
using System.Threading.Tasks;
using Grpc.Net.Client;
using MagicOnion.Client;
using Youverse.Core;
using Youverse.Core.Services.Identity;
using Youverse.Core.Services.Registry;
using Youverse.Core.Trie;

namespace Youverse.Hosting.IdentityRegistry
{
    public class IdentityRegistryRpc : IIdentityContextRegistry
    {
        private readonly Config _config;
        private readonly Trie<IdentityCertificate> _identityMap;

        public IdentityRegistryRpc(Config config)
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
                    IdentityCertificate identCert = Map(ident);
                    _identityMap.AddDomain(ident.DomainName, identCert);
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
            var path = Path.Combine(_config.TenantDataRootPath, domainName);
            var tempPath = Path.Combine(_config.TempTenantDataRootPath, domainName);
            var result = new TenantStorageConfig(Path.Combine(path, "data"), Path.Combine(tempPath, "temp"));
            return result;
        }

        private async Task<IdentityCertificate> LazyLoad(string domainName)
        {
            var cert = await GetClient().Get(domainName);
            if (cert == null)
            {
                Console.WriteLine($"No cert found on registry server for [{domainName}]");
                return null;
            }

            var identCert = Map(cert);
            if (null != identCert)
            {
                _identityMap.AddDomain(identCert.DomainName, identCert);
            }

            return identCert;
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
            var channel = GrpcChannel.ForAddress(_config.RegistryServerUri);
            var client = MagicOnionClient.Create<IRegistryRpcService>(channel);
            return client;
        }
    }
}