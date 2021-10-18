using Youverse.Core.Services.Identity;

namespace Youverse.Core.Services.Registry
{
    public interface IIdentityContextRegistry
    {
        void Initialize();
//        DotYouContext ResolveContext(string domainName);
        
        IdentityCertificate ResolveCertificate(string domainName);
        
        TenantStorageConfig ResolveStorageConfig(string domainName);
    }
}