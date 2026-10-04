using System;
using System.Threading;
using Odin.Core.Time;

namespace Odin.Services.Security.PasswordRecovery.Shamir;

/// <summary>
/// Lets one owner request at a time attempt a shard rotation, and none for <see cref="Cooldown"/> after
/// one fails. The owner auth handler checks for rotation on every request, so without this a burst of
/// requests after a recovery would each rotate, and a failing rotation would be retried on every one.
/// In memory, one per tenant: a restart costs one retry.
/// </summary>
public class ShardRotationGate
{
    public static readonly TimeSpan Cooldown = TimeSpan.FromHours(1);

    private readonly SemaphoreSlim _attempt = new(1, 1);
    private long _lastFailureMs;

    /// <summary>
    /// False while cooling down or while another request holds the gate; never waits. A true result
    /// must be paired with <see cref="Exit"/>.
    /// </summary>
    public bool TryEnter()
    {
        var coolingDown = UnixTimeUtc.Now().milliseconds - Interlocked.Read(ref _lastFailureMs) <
                          (long)Cooldown.TotalMilliseconds;
        return !coolingDown && _attempt.Wait(0);
    }

    public void Failed() => Interlocked.Exchange(ref _lastFailureMs, UnixTimeUtc.Now().milliseconds);

    public void Exit() => _attempt.Release();
}
