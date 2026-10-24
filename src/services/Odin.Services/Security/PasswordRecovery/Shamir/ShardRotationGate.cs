using System;
using System.Threading;
using System.Threading.Tasks;

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
    private readonly SemaphoreSlim _holder = new(1, 1);
    private long _lastFailureMs;

    // monotonic: a wall-clock jump must not stretch or cut the cooldown
    public ShardRotationGate() : this(() => Environment.TickCount64)
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
        if (IsCoolingDown() || !_holder.Wait(0))
        {
            return false;
        }

        // the previous holder may have failed between the check and the claim
        if (IsCoolingDown())
        {
            Exit();
            return false;
        }

        return true;
    }

    /// <summary>
    /// For a deal that must not be skipped (the phrase takeover): waits for the holder to leave, up to
    /// <paramref name="timeout"/>, and ignores the cooldown, which only throttles the auth handler's
    /// retries.  A true result must be paired with <see cref="Exit"/>.
    /// </summary>
    public Task<bool> WaitToEnterAsync(TimeSpan timeout) => _holder.WaitAsync(timeout);

    public void Failed() => Interlocked.Exchange(ref _lastFailureMs, _nowMs());

    public void Exit() => _holder.Release();

    private bool IsCoolingDown()
    {
        var lastFailureMs = Interlocked.Read(ref _lastFailureMs);
        return lastFailureMs != 0 && _nowMs() - lastFailureMs < CooldownMs;
    }
}
