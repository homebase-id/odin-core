using System;

namespace DotYou.IdentityRegistry
{
    public class IdentityRegistry : IIdentityContextRegistry
    {
        private Trie<IdentityCertificate> _identityMap = new Trie<IdentityCertificate>();
        
        public void Initialize()
        {
            throw new NotImplementedException();
        }

        public IdentityCertificate ResolveCertificate(string domainName)
        {
            throw new NotImplementedException();
        }

        public TenantStorageConfig ResolveStorageConfig(string domainName)
        {
            throw new NotImplementedException();
        }
    }
}