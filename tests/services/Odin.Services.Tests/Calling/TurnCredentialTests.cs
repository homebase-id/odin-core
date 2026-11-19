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
    public void RateLimiter_AllowsPermitsPerMinutePerApp()
    {
        var limiter = new TurnCredentialRateLimiter();
        var chat = Guid.NewGuid();
        var other = Guid.NewGuid();
        const long start = 1_000_000;

        for (var i = 0; i < TurnCredentialRateLimiter.PermitsPerMinute; i++)
        {
            Assert.That(limiter.TryAcquire(other, start + i), Is.True, $"login {i + 1}");
        }

        Assert.That(limiter.TryAcquire(other, start + 59_999), Is.False, "one over the limit within the minute");
        Assert.That(limiter.TryAcquire(chat, start + 59_999), Is.True, "another app has its own budget");
        Assert.That(limiter.TryAcquire(other, start + 60_000), Is.True, "a new minute starts over");
    }
}
