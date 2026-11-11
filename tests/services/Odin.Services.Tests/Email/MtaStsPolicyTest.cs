using NUnit.Framework;
using Odin.Services.Configuration;
using Odin.Services.Email;

namespace Odin.Services.Tests.Email;

public class MtaStsPolicyTest
{
    private static OdinConfiguration.TenantMailSection TenantMail(string[] mxNodes, string? mode = null, int? maxAgeSeconds = null) =>
        new()
        {
            MxNodes = [..mxNodes],
            MtaStsMode = mode ?? MtaStsPolicy.DefaultMode,
            MtaStsMaxAgeSeconds = maxAgeSeconds ?? MtaStsPolicy.DefaultMaxAgeSeconds
        };

    [Test]
    public void ItShouldBuildAWellFormedPolicy()
    {
        var policy = MtaStsPolicy.Build(TenantMail(["node-a.id.pub", "node-b.id.pub"]));

        Assert.That(policy, Is.EqualTo(
            "version: STSv1\n" +
            "mode: testing\n" +
            "mx: node-a.id.pub\n" +
            "mx: node-b.id.pub\n" +
            "max_age: 86400\n"));
    }

    // #1882: a fleet enforces by config
    [Test]
    public void ItShouldServeTheConfiguredModeAndMaxAge()
    {
        var policy = MtaStsPolicy.Build(TenantMail(["node-a.id.pub"], "enforce", 604800));

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
        // Different MX set, mode or max_age: different id, so receivers refetch the changed policy
        Assert.That(MtaStsPolicy.ComputeId(TenantMail(["node-a.id.pub"])), Is.Not.EqualTo(id));
        Assert.That(MtaStsPolicy.ComputeId(TenantMail(["node-a.id.pub", "node-b.id.pub"], mode: "enforce")), Is.Not.EqualTo(id));
        Assert.That(MtaStsPolicy.ComputeId(TenantMail(["node-a.id.pub", "node-b.id.pub"], maxAgeSeconds: 604800)), Is.Not.EqualTo(id));
    }
}
