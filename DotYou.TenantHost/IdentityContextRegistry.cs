using DotYou.Kernel;

namespace DotYou.TenantHost
{
    /// <summary>
    /// A registry of identities and their context by domain name.  This can be
    /// used to quickly lookup an identity for an incoming request
    /// </summary>
    public class IdentityContextRegistry
    {
        /// <summary>
        /// Resolves a context based on a given domain name
        /// </summary>
        /// <param name="domainName"></param>
        /// <returns></returns>
        public DotYouContext ResolveContext(string domainName)
        {
            //TODO
            return new DotYouContext();
        }
    }
}
