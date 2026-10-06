using System;
using System.Threading;
using Odin.Core.Time;

namespace Odin.Services.Security.PasswordRecovery.Shamir;

/// <summary>
/// Lets one owner request at a time attempt a shard rotation, and none for an hour after one fails.
/// The owner auth handler checks for rotation on every request, so without this a burst of requests
/// after a recovery would each rotate, and a failing rotation would be retried on every one.
/// In memory, one per tenant: a restart costs one retry.
/// </summary>
public class ShardRotationGate
{
    internal const long CooldownMs = 60 * 60 * 1000;

    private readonly Func<long> _nowMs;
    private int _busy;
    private long _lastFailureMs;

    public ShardRotationGate() : this(() => UnixTimeUtc.Now().milliseconds)
    {
    }

    /// <summary>For tests: <paramref name="nowMs"/> stands in for the clock.</summary>
    internal ShardRotationGate(Func<long> nowMs)
    {
        _nowMs = nowMs;
    }

    /// <summary>
    /// False while cooling down or while another request holds the gate; never waits. A true result
    /// must be paired with <see cref="Exit"/>.
    /// </summary>
    public bool TryEnter()
    {
        var lastFailureMs = Interlocked.Read(ref _lastFailureMs);
        if (lastFailureMs != 0 && _nowMs() - lastFailureMs < CooldownMs)
        {
            return false;
        }

        return Interlocked.CompareExchange(ref _busy, 1, 0) == 0;
    }

    public void Failed() => Interlocked.Exchange(ref _lastFailureMs, _nowMs());

    public void Exit() => Volatile.Write(ref _busy, 0);
}
