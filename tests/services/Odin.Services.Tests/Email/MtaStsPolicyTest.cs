using NUnit.Framework;
using Odin.Services.Email;

namespace Odin.Services.Tests.Email;

public class MtaStsPolicyTest
{
    [Test]
    public void ItShouldBuildAWellFormedPolicy()
    {
        var policy = MtaStsPolicy.Build(["node-a.id.pub", "node-b.id.pub"]);

        Assert.That(policy, Is.EqualTo(
            "version: STSv1\n" +
            "mode: testing\n" +
            "mx: node-a.id.pub\n" +
            "mx: node-b.id.pub\n" +
            "max_age: 86400\n"));
    }

    [Test]
    public void ItShouldComputeADeterministicIdThatChangesWithThePolicy()
    {
        var id = MtaStsPolicy.ComputeId(["node-a.id.pub", "node-b.id.pub"]);

        Assert.That(id, Has.Length.EqualTo(12));
        Assert.That(id, Does.Match("^[0-9a-f]{12}$"));
        // Same input, same id (receivers only refetch when the id changes)
        Assert.That(MtaStsPolicy.ComputeId(["node-a.id.pub", "node-b.id.pub"]), Is.EqualTo(id));
        // Different MX set, different id
        Assert.That(MtaStsPolicy.ComputeId(["node-a.id.pub"]), Is.Not.EqualTo(id));
    }
}
