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
    public List<string> Get()
    {
        var result = new List<string>();

        if (config.Registry.ProvisioningEnabled)
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
        if (config.Registry.ProvisioningEnabled && hostName == config.Registry.ProvisioningDomain)
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
