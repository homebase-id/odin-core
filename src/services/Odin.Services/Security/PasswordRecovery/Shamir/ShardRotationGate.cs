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
    private const long CooldownMs = 60 * 60 * 1000;

    private int _busy;
    private long _lastFailureMs;

    /// <summary>
    /// False while cooling down or while another request holds the gate; never waits. A true result
    /// must be paired with <see cref="Exit"/>.
    /// </summary>
    public bool TryEnter()
    {
        var lastFailureMs = Interlocked.Read(ref _lastFailureMs);
        if (lastFailureMs != 0 && UnixTimeUtc.Now().milliseconds - lastFailureMs < CooldownMs)
        {
            return false;
        }

        return Interlocked.CompareExchange(ref _busy, 1, 0) == 0;
    }

    public void Failed() => Interlocked.Exchange(ref _lastFailureMs, UnixTimeUtc.Now().milliseconds);

    public void Exit() => Volatile.Write(ref _busy, 0);
}
