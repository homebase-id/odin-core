using System;
using NUnit.Framework;
using Odin.Services.Email.Dkim;

namespace Odin.Services.Tests.Email.Dkim;

public class DkimTxtRecordTest
{
    [Test]
    public void ItShouldParseTheRecordsThisPlatformEmits()
    {
        foreach (var key in DkimKeyGenerator.GenerateKeys())
        {
            var parsed = DkimTxtRecord.TryParse(key.DnsRecordValue, out var kTag, out var publicKey);

            Assert.That(parsed, Is.True);
            Assert.That(kTag, Is.EqualTo(key.KTag));
            Assert.That(publicKey, Is.EqualTo(key.PublicKey));
        }
    }

    [Test]
    public void ItShouldDefaultKTagToRsa()
    {
        Assert.That(DkimTxtRecord.TryParse("v=DKIM1; p=" + Convert.ToBase64String([1, 2, 3]), out var kTag, out _), Is.True);
        Assert.That(kTag, Is.EqualTo("rsa"));
    }

    [Test]
    public void ItShouldTolerateWhitespaceInsideTheBase64()
    {
        var b64 = Convert.ToBase64String([1, 2, 3, 4]);
        var folded = b64.Insert(4, " ");
        Assert.That(DkimTxtRecord.TryParse($"v=DKIM1; k=ed25519; p={folded}", out _, out var publicKey), Is.True);
        Assert.That(publicKey, Is.EqualTo(new byte[] { 1, 2, 3, 4 }));
    }

    [Test]
    public void ItShouldRejectNonDkimValues()
    {
        Assert.That(DkimTxtRecord.TryParse("v=spf1 include:sendgrid.net -all", out _, out _), Is.False);
        Assert.That(DkimTxtRecord.TryParse("", out _, out _), Is.False);
        Assert.That(DkimTxtRecord.TryParse("v=DKIM1; k=rsa; p=", out _, out _), Is.False, "revoked key (empty p=)");
        Assert.That(DkimTxtRecord.TryParse("v=DKIM1; k=rsa; p=!!!notbase64!!!", out _, out _), Is.False);
    }
}
