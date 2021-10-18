namespace DotYou.IdentityRegistry
{
    public interface IIdentityContextRegistry
    {
        void Initialize();
//        DotYouContext ResolveContext(string domainName);
        
        IdentityCertificate ResolveCertificate(string domainName);
        
        TenantStorageConfig ResolveStorageConfig(string domainName);
    }
}