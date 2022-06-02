using System;
using System.Collections.Generic;
using System.IO;
using Youverse.Core.Identity;
using Youverse.Core.Trie;

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

        public DevelopmentIdentityContextRegistry(string dataStoragePath, string tempDataStoragePath)
        {
            if (!Directory.Exists(dataStoragePath))
                throw new InvalidDataException($"Could find or access path at [{dataStoragePath}]");

            if (!Directory.Exists(tempDataStoragePath))
                throw new InvalidDataException($"Could find or access path at [{tempDataStoragePath}]");

            _dataStoragePath = dataStoragePath;
            _tempDataStoragePath = tempDataStoragePath;
        }

        //this 
        private readonly Dictionary<Guid, string> _certificates = new();
        private readonly string _dataStoragePath;
        private readonly string _tempDataStoragePath;

        public void Initialize()
        {
            _certificates.Add(Guid.Parse("FBABCc39-1111-4442-9120-57ef89a11111"), "frodobaggins.me");
            _certificates.Add(Guid.Parse("55BBCc39-0001-0042-9120-57ef89a00000"), "samwisegamgee.me");
            _certificates.Add(Guid.Parse("00ABCc39-1111-4442-9120-57ef89a11111"), "frodo.digital");
            _certificates.Add(Guid.Parse("11BBCc39-0001-0042-9120-57ef89a00000"), "samwise.digital");

            foreach (var c in _certificates)
            {
                this.EnsureCertificateInFolder(c);
                _identityMap.AddDomain(c.Value, c.Key);
            }
        }

        private void EnsureCertificateInFolder(KeyValuePair<Guid, string> kvp)
        {
            Guid id = kvp.Key;
            string domain = kvp.Value;

            //lookup certificate from source
            Guid domainId = CertificateResolver.CalculateDomainId((DotYouIdentity) domain);
            string domainRootPath = Path.Combine(_dataStoragePath, id.ToString(), "ssl", domainId.ToString());
            string destCertPath = Path.Combine(domainRootPath, "certificate.crt");
            string destKeyPath = Path.Combine(domainRootPath, "private.key");

            Directory.CreateDirectory(domainRootPath);

            string sourceCertPath = Path.Combine(Environment.CurrentDirectory, "https", domain, "certificate.crt");

            //only copy if needed
            if (!File.Exists(destCertPath))
            {
                if (!File.Exists(sourceCertPath))
                {
                    throw new Exception($"Cannot find [{sourceCertPath}]");
                }

                File.Copy(sourceCertPath, destCertPath);
            }

            string sourceKeyPath = Path.Combine(Environment.CurrentDirectory, "https", domain, "private.key");

            if (!File.Exists(destKeyPath))
            {
                if (!File.Exists(sourceKeyPath))
                {
                    throw new Exception($"Cannot find [{sourceKeyPath}]");
                }

                File.Copy(sourceKeyPath, destKeyPath);
            }
        }

        public Guid ResolveId(string domainName)
        {
            var key = _identityMap.LookupName(domainName);

            if (key == Guid.Empty)
            {
                throw new InvalidTenantException($"No tenant with domain [{domainName}]");
            }

            return key;
        }
    }
}