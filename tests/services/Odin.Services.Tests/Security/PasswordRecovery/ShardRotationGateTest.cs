using NUnit.Framework;
using Odin.Services.Security.PasswordRecovery.Shamir;

namespace Odin.Services.Tests.Security.PasswordRecovery;

/// <summary>
/// The gate's two guarantees without a host: one rotation attempt at a time, and none for an hour
/// after one fails. The end-to-end tests (<c>ShardRotationGateTests</c> in Odin.Hosting.Tests.V2) need
/// recovery nonces that only exist in Debug, so these are what Release CI runs (#1866).
/// </summary>
public class ShardRotationGateTest
{
    private long _now = 1_000_000;

    private ShardRotationGate NewGate() => new(() => _now);

    [Test]
    public void OnlyOneHolderAtATime()
    {
        var gate = NewGate();

        Assert.That(gate.TryEnter(), Is.True);
        Assert.That(gate.TryEnter(), Is.False, "entered while held");

        gate.Exit();
        Assert.That(gate.TryEnter(), Is.True, "not released by Exit");
    }

    [Test]
    public void FailureHoldsOffUntilTheCooldownHasPassed()
    {
        var gate = NewGate();

        Assert.That(gate.TryEnter(), Is.True);
        gate.Failed();
        gate.Exit();

        _now += ShardRotationGate.CooldownMs - 1;
        Assert.That(gate.TryEnter(), Is.False, "entered during the cooldown");

        _now += 1;
        Assert.That(gate.TryEnter(), Is.True, "still held off after the cooldown");
    }

    [Test]
    public void NoFailureMeansNoCooldown()
    {
        var gate = NewGate();

        Assert.That(gate.TryEnter(), Is.True);
        gate.Exit();
        Assert.That(gate.TryEnter(), Is.True);
    }
}
