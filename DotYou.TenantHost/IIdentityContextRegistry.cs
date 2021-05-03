using DotYou.Kernel;

namespace DotYou.TenantHost
{
    internal interface IIdentityContextRegistry
    {
        void Initialize();
        DotYouContext ResolveContext(string domainName);
    }
}