using System.ComponentModel;
using Dawn;
using Youverse.Core;
using Youverse.Core.Util;

namespace Youverse.Provisioning.Services.Registry
{
    public class IdentityRegistry : IIdentityRegistry, IDisposable
    {
        private readonly LiteDBSingleCollectionStorage<IdentityRegistration> _storage;
        const string CollectionName = "IdentityRegistry";

        public IdentityRegistry(ILogger<IdentityRegistration> logger, ProvisioningConfig config)
        {
            string path = Path.Combine(config.DataRootPath, CollectionName);
            _storage = new LiteDBSingleCollectionStorage<IdentityRegistration>(logger, path, CollectionName);
            _storage.EnsureIndex(key => key.DomainKey, true);
        }

        public async Task<bool> IsDomainRegistered(string domain)
        {
            Guard.Argument(domain, nameof(domain)).NotNull().NotEmpty();
            Guid key =HashUtil.ReduceSHA256Hash(domain);
            var reg = await _storage.FindOne(r => r.DomainKey == key);
            return null != reg;
        }

        public async Task Add(IdentityRegistration reg)
        {
            Guard.Argument(reg, nameof(reg)).NotNull();
            Guard.Argument(reg.DomainName, nameof(reg.DomainName)).NotNull().NotEmpty();

            Guard.Argument(reg.CertificateRenewalInfo, nameof(reg.CertificateRenewalInfo)).NotNull();

            Guard.Argument(reg.CertificateRenewalInfo.CertificateSigningRequest, nameof(reg.CertificateRenewalInfo.CertificateSigningRequest)).NotNull();
            Guard.Argument(reg.CertificateRenewalInfo.CertificateSigningRequest.Locality, nameof(reg.CertificateRenewalInfo.CertificateSigningRequest.Locality)).NotNull().NotEmpty();
            Guard.Argument(reg.CertificateRenewalInfo.CertificateSigningRequest.State, nameof(reg.CertificateRenewalInfo.CertificateSigningRequest.State)).NotNull().NotEmpty();
            Guard.Argument(reg.CertificateRenewalInfo.CertificateSigningRequest.CountryName, nameof(reg.CertificateRenewalInfo.CertificateSigningRequest.CountryName)).NotNull().NotEmpty();
            Guard.Argument(reg.CertificateRenewalInfo.CertificateSigningRequest.Organization, nameof(reg.CertificateRenewalInfo.CertificateSigningRequest.Organization)).NotNull().NotEmpty();
            Guard.Argument(reg.CertificateRenewalInfo.CertificateSigningRequest.OrganizationUnit, nameof(reg.CertificateRenewalInfo.CertificateSigningRequest.OrganizationUnit)).NotNull().NotEmpty();

            Guard.Argument(reg.PublicKeyCertificateRelativePath, nameof(reg.PublicKeyCertificateRelativePath)).NotNull().NotEmpty().Require(File.Exists);
            Guard.Argument(reg.PrivateKeyRelativePath, nameof(reg.PrivateKeyRelativePath)).NotNull().NotEmpty().Require(File.Exists);
            Guard.Argument(reg.FullChainCertificateRelativePath, nameof(reg.FullChainCertificateRelativePath)).NotNull().NotEmpty().Require(File.Exists);

            await _storage.Save(reg);

            //TODO: might need to notify something for a cache update 
        }

        public async Task<PagedResult<IdentityRegistration>> GetList(PageOptions pageOptions)
        {
            var list = await _storage.GetList(pageOptions, ListSortDirection.Ascending, reg => reg.DomainName);
            return list;
        }

        public async Task<IdentityRegistration> Get(string domainName)
        {
            var key =HashUtil.ReduceSHA256Hash(domainName);
            var result = await _storage.FindOne(reg => reg.DomainKey == key);
            return result;
        }

        public void Dispose()
        {
            _storage?.Dispose();
        }
    }
}