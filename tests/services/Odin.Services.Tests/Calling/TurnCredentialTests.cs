using System;
using NUnit.Framework;
using Odin.Core.Identity;
using Odin.Core.Time;
using Odin.Services.Calling;

namespace Odin.Services.Tests.Calling;

public class TurnCredentialTests
{
    private const string Secret = "odin-turn-test-secret-0123456789abcdef";

    [Test]
    public void Create_MatchesAVectorComputedOutsideThisCode()
    {
        // python3 -c 'import hmac,hashlib,base64; print(base64.b64encode(hmac.new(
        //   b"odin-turn-test-secret-0123456789abcdef", b"1760000300:frodo.dotyou.cloud", hashlib.sha1).digest()).decode())'
        var login = TurnCredential.Create(new OdinId("frodo.dotyou.cloud"), Secret,
            new UnixTimeUtc(1_760_000_000_000), 300);

        Assert.That(login.Username, Is.EqualTo("1760000300:frodo.dotyou.cloud"));
        Assert.That(login.Credential, Is.EqualTo("8L3kYjrCU3Hphx46LFGtqgyHzAY="));
        Assert.That(login.ExpiresAt, Is.EqualTo(new UnixTimeUtc(1_760_000_300_000)));
    }

    [Test]
    public void Create_ExpiryIsWholeSecondsAfterNow()
    {
        var login = TurnCredential.Create(new OdinId("frodo.dotyou.cloud"), Secret,
            new UnixTimeUtc(1_760_000_000_999), 60);

        Assert.That(login.Username, Is.EqualTo("1760000060:frodo.dotyou.cloud"),
            "the username carries the expiry in whole seconds, which is what coturn compares");
        Assert.That(login.ExpiresAt, Is.EqualTo(new UnixTimeUtc(1_760_000_060_000)),
            "ExpiresAt says the same moment as the username");
    }

    [Test]
    public void RateLimiter_AllowsPermitsPerMinute_ThenSaysWhenToRetry()
    {
        var limiter = new TurnCredentialRateLimiter();
        var start = new UnixTimeUtc(1_760_000_000_000);

        for (var i = 0; i < TurnCredentialRateLimiter.PermitsPerMinute; i++)
        {
            Assert.That(limiter.TryAcquire(start.AddMilliseconds(i), out _), Is.True, $"login {i + 1}");
        }

        var refused = limiter.TryAcquire(start.AddSeconds(15), out var retryAfter);
        Assert.That(refused, Is.False, "one over the limit");
        Assert.That(retryAfter, Is.EqualTo(TimeSpan.FromSeconds(45)));

        Assert.That(limiter.TryAcquire(start.AddSeconds(60), out _), Is.True, "a new minute starts over");
    }
}
