using System.Collections;
using System.Collections.Generic;
using Youverse.Core.Services.Identity;

namespace Youverse.Core.Services.Registry
{
    public interface IIdentityContextRegistry
    {
        void Initialize();
        
        IdentityCertificate ResolveCertificate(string domainName);
        
        TenantStorageConfig ResolveStorageConfig(string domainName);

        /// <summary>
        /// Returns a list of registered domains
        /// TODO: this is temp until we evaluate if this is the right place for it
        /// </summary>
        /// <returns></returns>
        IEnumerable<string> GetDomains();

    }
}