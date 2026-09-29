using System.Collections.Generic;
using Odin.Services.Configuration;

namespace Odin.Services.Certificate;

public interface ISystemDomains
{
    List<string> Get();
    bool IsKnownSystemDomain(string hostName);
}

//

public class SystemDomains(OdinConfiguration config) : ISystemDomains
{
    // The provisioning domain also serves the payload move endpoint, whether or not this host provisions,
    // so it needs its certificate for either
    private bool ServesProvisioningDomain => config.Registry.ProvisioningEnabled || config.PayloadMove.SourceEnabled;

    public List<string> Get()
    {
        var result = new List<string>();

        if (ServesProvisioningDomain)
        {
            result.Add(config.Registry.ProvisioningDomain);
        }

        if (config.Admin.ApiEnabled)
        {
            result.Add(config.Admin.Domain);
        }

        return result;
    }

    //

    public bool IsKnownSystemDomain(string hostName)
    {
        if (ServesProvisioningDomain && hostName == config.Registry.ProvisioningDomain)
        {
            return true;
        }

        if (config.Admin.ApiEnabled && hostName == config.Admin.Domain)
        {
            return true;
        }

        return false;
    }
}
