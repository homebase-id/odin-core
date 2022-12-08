using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Youverse.Core.Identity;
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
    public class DevelopmentIdentityRegistry : IIdentityRegistry
    {
        private Trie<Guid> _identityMap = new Trie<Guid>();

        public DevelopmentIdentityRegistry(string tenantDataRootPath, string tempDataStoragePath)
        {
            if (!Directory.Exists(tenantDataRootPath))
                throw new InvalidDataException($"Could find or access path at [{tenantDataRootPath}]");

            if (!Directory.Exists(tempDataStoragePath))
                throw new InvalidDataException($"Could find or access path at [{tempDataStoragePath}]");

            _tenantDataRootPath = tenantDataRootPath;
            _tempDataStoragePath = tempDataStoragePath;
        }

        //this 
        private readonly Dictionary<Guid, string> _certificates = new();
        private readonly string _tenantDataRootPath;
        private readonly string _tempDataStoragePath;

        public void Initialize()
        {
            //demo server
            _certificates.Add(HashUtil.ReduceSHA256Hash("frodobaggins-me"), "frodobaggins.me");
            _certificates.Add(HashUtil.ReduceSHA256Hash("samwisegamgee-me"), "samwisegamgee.me");

            //local development
            _certificates.Add(HashUtil.ReduceSHA256Hash("frodo-digital"), "frodo.digital");
            _certificates.Add(HashUtil.ReduceSHA256Hash("samwise-digital"), "samwise.digital");
            _certificates.Add(HashUtil.ReduceSHA256Hash("merry-onekin-io"), "merry.onekin.io");
            _certificates.Add(HashUtil.ReduceSHA256Hash("pippin-onekin-io"), "pippin.onekin.io");

            //app-development 
            _certificates.Add(HashUtil.ReduceSHA256Hash("legolas-onekin-io"), "legolas.onekin.io");
            _certificates.Add(HashUtil.ReduceSHA256Hash("gimli-onekin-io"), "gimli.onekin.io");
            _certificates.Add(HashUtil.ReduceSHA256Hash("aragorn-onekin-io"), "aragorn.onekin.io");


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
            Guid domainId = CertificateResolver.CalculateDomainId((DotYouIdentity)domain);
            string domainRootPath = Path.Combine(_tenantDataRootPath, id.ToString(), "ssl", domainId.ToString());
            string destCertPath = Path.Combine(domainRootPath, "certificate.crt");
            string destKeyPath = Path.Combine(domainRootPath, "private.key");

            Directory.CreateDirectory(domainRootPath);

            string sourceCertPath = Path.Combine(Environment.CurrentDirectory, "https", domain, "certificate.crt");
            File.Copy(sourceCertPath, destCertPath, true);

            string sourceKeyPath = Path.Combine(Environment.CurrentDirectory, "https", domain, "private.key");
            File.Copy(sourceKeyPath, destKeyPath, true);
        }

        public Guid ResolveId(string domain)
        {
            var key = _identityMap.LookupName(domain);

            if (key == Guid.Empty)
            {
                throw new InvalidTenantClientException($"No tenant with domain '{domain}'");
            }

            return key;
        }

        public Task<bool> IsIdentityRegistered(string domain)
        {
            throw new NotImplementedException();
        }

        public Task<bool> HasValidCertificate(string domain)
        {
            throw new NotImplementedException();
        }

        public Task AddRegistration(IdentityRegistrationRequest request)
        {
            throw new NotImplementedException();
        }

        public Task<PagedResult<IdentityRegistration>> GetList(PageOptions pageOptions)
        {
            throw new NotImplementedException();
        }

        public Task<IdentityRegistration> Get(string domainName)
        {
            throw new NotImplementedException();
        }
    }
}