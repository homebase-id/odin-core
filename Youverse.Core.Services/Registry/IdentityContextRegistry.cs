using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Youverse.Core.Services.Identity;
using Youverse.Core.Trie;

namespace Youverse.Core.Services.Registry
{
    /// <summary>
    /// A registry of identities and their context by domain name.  This can be
    /// used to quickly lookup an identity for an incoming request
    /// </summary>
    /// Note: this is marked internal to ensure code running in a given instance 
    /// of any class in Youverse.Core.Services.* cannot access other Identities
    public class IdentityContextRegistry : IIdentityContextRegistry
    {
        private Trie<Guid> _identityMap = new Trie<Guid>();
        private List<string> _tempDomains = new();

        private string _dataStoragePath;
        private string _tempDataStoragePath;

        public IdentityContextRegistry(string dataStoragePath, string tempDataStoragePath)
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
        private static readonly IdentityCertificate RootIdentityCertificate =
            new IdentityCertificate(Guid.Parse("ca67c239-2e05-42ca-9120-57ef89ac05db"), "youfoundation.id",
                new NameInfo() { GivenName = "You Foundation", Surname = "System User" },
                new CertificateLocation()
                {
                    CertificatePath = Path.Combine(Environment.CurrentDirectory, "https", "youfoundation.id", "certificate.cer"),
                    PrivateKeyPath = Path.Combine(Environment.CurrentDirectory, "https", "youfoundation.id", "private.key")
                });

        public void Initialize()
        {
            IdentityCertificate samwise =
                new(Guid.NewGuid(), "samwisegamgee.me", new NameInfo() { GivenName = "Samwise", Surname = "Gamgee" },
                    new CertificateLocation()
                    {
                        CertificatePath = Path.Combine(Environment.CurrentDirectory, "https", "samwisegamgee.me", "samwisegamgee_me.crt"),
                        PrivateKeyPath = Path.Combine(Environment.CurrentDirectory, "https", "samwisegamgee.me", "samwisegamgee.key")
                    });

            IdentityCertificate frodo =
                new(Guid.NewGuid(), "frodobaggins.me", new NameInfo() { GivenName = "Frodo", Surname = "Baggins" },
                    new CertificateLocation()
                    {
                        CertificatePath = Path.Combine(Environment.CurrentDirectory, "https", "frodobaggins.me", "frodobaggins_me.crt"),
                        PrivateKeyPath = Path.Combine(Environment.CurrentDirectory, "https", "frodobaggins.me", "frodobaggins_me.key")
                    });

            // IdentityCertificate gandalf =
            //     new(Guid.NewGuid(), "gandalf.middleearth.life", new NameInfo() {GivenName = "Gandalf", Surname = "teh White"},
            //         new CertificateLocation()
            //         {
            //             CertificatePath = Path.Combine(Environment.CurrentDirectory, "https", "gandalf.middleearth.life", "certificate.cer"),
            //             PrivateKeyPath = Path.Combine(Environment.CurrentDirectory, "https", "gandalf.middleearth.life", "private.key")
            //         });
            //
            // IdentityCertificate todd =
            //     new(Guid.NewGuid(), "toddmitchell.me", new NameInfo() {GivenName = "Todd", Surname = "Mitchell"},
            //         new CertificateLocation()
            //         {
            //             CertificatePath = Path.Combine(Environment.CurrentDirectory, "https", "gandalf.middleearth.life", "certificate.cer"),
            //             PrivateKeyPath = Path.Combine(Environment.CurrentDirectory, "https", "gandalf.middleearth.life", "private.key")
            //         });
            //
            // IdentityCertificate michael =
            //     new(Guid.NewGuid(), "gandalf.middleearth.life", new NameInfo() {GivenName = "Gandalf", Surname = "teh White"},
            //         new CertificateLocation()
            //         {
            //             CertificatePath = Path.Combine(Environment.CurrentDirectory, "https", "gandalf.middleearth.life", "certificate.cer"),
            //             PrivateKeyPath = Path.Combine(Environment.CurrentDirectory, "https", "gandalf.middleearth.life", "private.key")
            //         });
            //

            //_certificates.Add(RootIdentityCertificate.Key, RootIdentityCertificate);
            _certificates.Add(samwise.Key, samwise);
            _certificates.Add(frodo.Key, frodo);
            // _certificates.Add(gandalf.Key, gandalf);

            foreach (var c in _certificates.Values)
            {
                this.CacheDomain(c);
            }
        }
        
        public IdentityCertificate ResolveCertificate(string domainName)
        {
            var key = _identityMap.LookupName(domainName);

            if (key == Guid.Empty)
            {
                return null;
            }

            if (!_certificates.TryGetValue(key, out var cert))
            {
                throw new InvalidDataException($"The Trie map contains a key for domain {domainName} but it is not cached in the dictionary.");
            }

            return cert;
        }

        public TenantStorageConfig ResolveStorageConfig(string domainName)
        {
            var path = Path.Combine(_dataStoragePath, domainName);
            var tempPath = Path.Combine(_tempDataStoragePath, domainName);
            var result = new TenantStorageConfig(Path.Combine(path, "data"), Path.Combine(tempPath, "temp"));
            return result;
        }

        public IEnumerable<string> GetDomains()
        {
            return _tempDomains;
        }
        
        private void CacheDomain(IdentityCertificate c)
        {
            Console.WriteLine($"Caching cert [{c.DomainName}] in Trie");
            _identityMap.AddDomain(c.DomainName, c.Key);
            _tempDomains.Add(c.DomainName);
        }
    }
}