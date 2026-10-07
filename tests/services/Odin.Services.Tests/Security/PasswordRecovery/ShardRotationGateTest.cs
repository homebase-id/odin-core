using NUnit.Framework;
using Odin.Services.Security.PasswordRecovery.Shamir;

namespace Odin.Services.Tests.Security.PasswordRecovery;

#nullable enable

/// <summary>
/// The gate's two guarantees without a host: one rotation attempt at a time, and none for an hour
/// after one fails. The end-to-end tests (<c>FailedShardRotationTests</c> in Odin.Hosting.Tests.V2) need
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
    public void AFailureBetweenTheCheckAndTheClaimStillHoldsOff()
    {
        System.Action? onClockRead = null;
        var gate = new ShardRotationGate(() =>
        {
            var hook = onClockRead;
            onClockRead = null;
            hook?.Invoke();
            return _now;
        });

        // an old failure, long expired, so the next check reads the clock
        Assert.That(gate.TryEnter(), Is.True);
        gate.Failed();
        gate.Exit();
        _now += ShardRotationGate.CooldownMs;

        Assert.That(gate.TryEnter(), Is.True, "the holder");

        // while the next request checks the cooldown, the holder fails and lets go
        onClockRead = () =>
        {
            gate.Failed();
            gate.Exit();
        };

        Assert.That(gate.TryEnter(), Is.False, "retried straight after a failure");
        Assert.That(gate.TryEnter(), Is.False, "left the gate held or the cooldown off");
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
