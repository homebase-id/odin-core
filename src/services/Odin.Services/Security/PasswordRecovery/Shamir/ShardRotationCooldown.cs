using System;
using System.Threading;
using Odin.Core.Time;

namespace Odin.Services.Security.PasswordRecovery.Shamir;

/// <summary>
/// Remembers a failed shard rotation so the owner auth handler, which checks for rotation on every
/// request, does not retry it on every request. In memory, one per tenant: a restart costs one retry.
/// </summary>
public class ShardRotationCooldown
{
    public static readonly TimeSpan Period = TimeSpan.FromHours(1);

    private long _lastFailureMs;

    public bool IsCoolingDown =>
        UnixTimeUtc.Now().milliseconds - Interlocked.Read(ref _lastFailureMs) < (long)Period.TotalMilliseconds;

    public void Failed() => Interlocked.Exchange(ref _lastFailureMs, UnixTimeUtc.Now().milliseconds);
}
