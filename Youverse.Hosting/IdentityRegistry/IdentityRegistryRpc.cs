using System;
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
        private readonly Trie<Guid> _identityMap;

        public IdentityRegistryRpc(Configuration config)
        {
            _config = config;
            _identityMap = new Trie<Guid>();
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

        public Guid ResolveId(string domainName)
        {
            Guid id = _identityMap.LookupName(domainName);
            if (id == Guid.Empty)
            {
                id = LazyLoad(domainName).ConfigureAwait(false).GetAwaiter().GetResult().GetValueOrDefault();
            }

            if (id == Guid.Empty)
            {
                throw new InvalidTenantException($"Not tenant with domain [{domainName}]");
            }

            return id;
        }

        private Guid CacheDomain(IdentityRegistration ident)
        {
            _identityMap.AddDomain(ident.DomainName, ident.Id);
            return ident.Id;
        }

        private async Task<Guid?> LazyLoad(string domainName)
        {
            var cert = await GetClient().Get(domainName);
            if (cert == null)
            {
                Console.WriteLine($"No cert found on registry server for [{domainName}]");
                return null;
            }

            return CacheDomain(cert);
        }

        private IRegistryRpcService GetClient()
        {
            var channel = GrpcChannel.ForAddress(_config.Host.RegistryServerUri);
            var client = MagicOnionClient.Create<IRegistryRpcService>(channel);
            return client;
        }
    }
}