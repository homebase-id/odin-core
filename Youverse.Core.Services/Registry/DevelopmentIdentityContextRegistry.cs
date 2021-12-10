using System;
using System.Collections.Generic;
using System.IO;
using Youverse.Core.Services.Identity;
using Youverse.Core.Trie;
using Youverse.Core.Util;

namespace Youverse.Core.Services.Registry
{
    /// <summary>
    /// A registry of identities and their context by domain name.  This can be
    /// used to quickly lookup an identity for an incoming request
    /// </summary>
    /// Note: this is marked internal to ensure code running in a given instance 
    /// of any class in Youverse.Core.Services.* cannot access other Identities
    public class DevelopmentIdentityContextRegistry : IIdentityContextRegistry
    {
        private Trie<Guid> _identityMap = new Trie<Guid>();

        private string _dataStoragePath;
        private string _tempDataStoragePath;

        public DevelopmentIdentityContextRegistry(string dataStoragePath, string tempDataStoragePath)
        {
            if (!Directory.Exists(dataStoragePath))
                throw new InvalidDataException($"Could find or access path at [{dataStoragePath}]");

            if (!Directory.Exists(tempDataStoragePath))
                throw new InvalidDataException($"Could find or access path at [{tempDataStoragePath}]");

            _dataStoragePath = dataStoragePath;
            _tempDataStoragePath = tempDataStoragePath;
        }

        //temporary until the Trie supports Generics
        private Dictionary<Guid, IdentityCertificate> _certificates = new();

        /// <summary>
        /// Hard coded identity which lets you boostrap your system when you have no other sites
        /// Note: for a production system this must be moved to configuration.
        /// </summary>
        private static readonly IdentityCertificate RootIdentityCertificate = new(Guid.Parse("ca67c239-2e05-42ca-9120-57ef89ac05db"), "youfoundation.id");

        public void Initialize()
        {
            IdentityCertificate samwise = new(Guid.Parse("AABBCc39-0001-0042-9120-57ef89a00000"), "samwisegamgee.me");
            IdentityCertificate frodo = new(Guid.Parse("AABBCc39-1111-4442-9120-57ef89a11111"), "frodobaggins.me");

            //_certificates.Add(RootIdentityCertificate.Key, RootIdentityCertificate);
            _certificates.Add(samwise.Key, samwise);
            _certificates.Add(frodo.Key, frodo);

            foreach (var c in _certificates.Values)
            {
                this.CacheDomain(c);
            }
        }

        public Guid ResolveId(string domainName)
        {
            var key = _identityMap.LookupName(domainName);

            if (key == Guid.Empty)
            {
                throw new InvalidTenantException($"Not tenant with domain [{domainName}]");
            }

            return key;
        }

        private void CacheDomain(IdentityCertificate c)
        {
            Console.WriteLine($"Caching cert [{c.DomainName}] in Trie");
            _identityMap.AddDomain(c.DomainName, c.Key);
        }
    }
}