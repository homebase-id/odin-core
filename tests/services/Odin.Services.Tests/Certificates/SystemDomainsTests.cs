using NUnit.Framework;
using Odin.Services.Certificate;
using Odin.Services.Configuration;

namespace Odin.Services.Tests.Certificates;

public class SystemDomainsTests
{
    private const string ProvisioningDomain = "provisioning.example.com";

    [TestCase(true, false, true)]
    [TestCase(false, true, true)]
    [TestCase(false, false, false)]
    public void TheProvisioningDomainIsServedWhenProvisioningOrAsAPayloadMoveSource(bool provisioning, bool payloadMoveSource, bool served)
    {
        var domains = new SystemDomains(new OdinConfiguration
        {
            Registry = new OdinConfiguration.RegistrySection { ProvisioningDomain = ProvisioningDomain, ProvisioningEnabled = provisioning },
            PayloadMove = new OdinConfiguration.PayloadMoveSection { SourceEnabled = payloadMoveSource },
            Admin = new OdinConfiguration.AdminSection { ApiEnabled = false }
        });

        // Without a certificate the payload move endpoint cannot be reached on a host that does not provision
        Assert.That(domains.Get().Contains(ProvisioningDomain), Is.EqualTo(served));
        Assert.That(domains.IsKnownSystemDomain(ProvisioningDomain), Is.EqualTo(served));
    }
}
