using NUnit.Framework;
using Odin.Services.Configuration;
using Odin.Services.Email;

namespace Odin.Services.Tests.Email;

public class MtaStsPolicyTest
{
    private static OdinConfiguration.TenantMailSection TenantMail(string[] mxNodes, string mode = "testing", int maxAge = 86400) =>
        new() { MxNodes = [..mxNodes], MtaStsMode = mode, MtaStsMaxAgeSeconds = maxAge };

    [Test]
    public void ItShouldBuildAWellFormedPolicy()
    {
        var policy = MtaStsPolicy.Build(new OdinConfiguration.TenantMailSection { MxNodes = ["node-a.id.pub", "node-b.id.pub"] });

        // The section's defaults are the policy served before mode and max_age were configurable
        Assert.That(policy, Is.EqualTo(
            "version: STSv1\n" +
            "mode: testing\n" +
            "mx: node-a.id.pub\n" +
            "mx: node-b.id.pub\n" +
            "max_age: 86400\n"));
    }

    [Test]
    public void ItShouldServeTheConfiguredModeAndMaxAge()
    {
        var policy = MtaStsPolicy.Build(TenantMail(["node-a.id.pub"], mode: "enforce", maxAge: 604800));

        Assert.That(policy, Is.EqualTo(
            "version: STSv1\n" +
            "mode: enforce\n" +
            "mx: node-a.id.pub\n" +
            "max_age: 604800\n"));
    }

    [Test]
    public void ItShouldComputeADeterministicIdThatChangesWithThePolicy()
    {
        var id = MtaStsPolicy.ComputeId(TenantMail(["node-a.id.pub", "node-b.id.pub"]));

        Assert.That(id, Has.Length.EqualTo(12));
        Assert.That(id, Does.Match("^[0-9a-f]{12}$"));
        // Same input, same id (receivers only refetch when the id changes)
        Assert.That(MtaStsPolicy.ComputeId(TenantMail(["node-a.id.pub", "node-b.id.pub"])), Is.EqualTo(id));
        // Different MX set, mode or max_age: a different policy, so a different id
        Assert.That(MtaStsPolicy.ComputeId(TenantMail(["node-a.id.pub"])), Is.Not.EqualTo(id));
        Assert.That(MtaStsPolicy.ComputeId(TenantMail(["node-a.id.pub", "node-b.id.pub"], mode: "enforce")), Is.Not.EqualTo(id));
        Assert.That(MtaStsPolicy.ComputeId(TenantMail(["node-a.id.pub", "node-b.id.pub"], maxAge: 604800)), Is.Not.EqualTo(id));
    }
}
