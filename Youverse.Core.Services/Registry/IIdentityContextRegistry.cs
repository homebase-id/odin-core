using System;

namespace Youverse.Core.Services.Registry
{
    public interface IIdentityContextRegistry
    {
        void Initialize();

        Guid ResolveId(string domainName);
        
    }
}