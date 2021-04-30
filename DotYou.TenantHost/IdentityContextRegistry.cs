using DotYou.Kernel;
using DotYou.Kernel.Services.Identity;
using System;
using System.Collections.Generic;
using System.IO;

namespace DotYou.TenantHost
{
    /// <summary>
    /// A registry of identities and their context by domain name.  This can be
    /// used to quickly lookup an identity for an incoming request
    /// </summary>
    /// Note: this is marked internal to ensure code running in a given instance 
    /// of any class in DotYou.Kernel.* cannot access other Identities
    internal class IdentityContextRegistry
    {

        private Trie _identityMap = new Trie();

        //temporary until the Trie supports Generics
        private Dictionary<Guid, IdentityCertificate> _certificates = new Dictionary<Guid, IdentityCertificate>(); 
        
        /// <summary>
        /// Hard coded identity which lets you boostrap your system when you have no other sites
        /// Note: for a production system this must be moved to configuration.
        /// </summary>
        private static readonly IdentityCertificate RootIdentityCertificate = new IdentityCertificate(Guid.Parse("ca67c239-2e05-42ca-9120-57ef89ac05db"), "youfoundation.id")
        {
            Location = new CertificateLocation()
            {
                CertificatePath = Path.Combine(Environment.CurrentDirectory, "https", "youfoundation.id", "certificate.cer"),
                PrivateKeyPath = Path.Combine(Environment.CurrentDirectory, "https", "youfoundation.id", "private.key"),
            }
        };

        /// <summary>
        /// Resolves a context based on a given domain name
        /// </summary>
        /// <param name="domainName"></param>
        /// <returns></returns>
        public DotYouContext ResolveContext(string domainName)
        {

            var key = _identityMap.lookupName(domainName);

            if (key == Guid.Empty)
            {
                return null;
            }

            IdentityCertificate cert;
            if(!_certificates.TryGetValue(key, out cert))
            {
                throw new InvalidDataException($"The Trie map contains a key for domain {domainName} but it is not cached in the dictionary.");
            }

            return new DotYouContext()
            {
                Certificate = cert
            };
        }

        public void Initialize()
        {

            IdentityCertificate samwise = new IdentityCertificate(Guid.NewGuid(), "samwisegamgee.me")
            {
                Location = new CertificateLocation()
                {
                    CertificatePath = Path.Combine(Environment.CurrentDirectory, "https", "samwisegamgee.me", "certificate.crt"),
                    PrivateKeyPath = Path.Combine(Environment.CurrentDirectory, "https", "samwisegamgee.me", "private.key"),
                }
            };

            IdentityCertificate frodo = new IdentityCertificate(Guid.NewGuid(), "frodobaggins.me")
            {
                
                Location = new CertificateLocation()
                {
                    CertificatePath = Path.Combine(Environment.CurrentDirectory, "https", "frodobaggins.me", "certificate.crt"),
                    PrivateKeyPath = Path.Combine(Environment.CurrentDirectory, "https", "frodobaggins.me", "private.key"),
                }
            };

            _certificates.Add(RootIdentityCertificate.Key, RootIdentityCertificate);
            _certificates.Add(samwise.Key, samwise);
            _certificates.Add(frodo.Key, frodo);

            foreach (var c in _certificates.Values)
            {
                Console.WriteLine($"Caching cert [{c.DomainName}] in Trie");
                this._identityMap.addName(c.DomainName, c.Key);
            }
        }
    }
}
