using System.Linq;
using NUnit.Framework;
using Odin.Services.Email.Dkim;

namespace Odin.Services.Tests.Email.Dkim;

public class DkimDnsRecordsTest
{
    [Test]
    public void ItShouldShapeKeysAsOptionalTxtRecords()
    {
        var keys = DkimKeyGenerator.GenerateKeys();
        var records = DkimDnsRecords.ToDnsConfigs("frodo.dotyou.cloud", keys);

        Assert.That(records.Count, Is.EqualTo(2));

        var s1 = records.Single(r => r.Name == "s1._domainkey");
        Assert.That(s1.Type, Is.EqualTo("TXT"));
        Assert.That(s1.Domain, Is.EqualTo("s1._domainkey.frodo.dotyou.cloud"));
        Assert.That(s1.Value, Does.StartWith("v=DKIM1; k=ed25519; p="));
        Assert.That(s1.AltValue, Is.EqualTo(s1.Value));
        Assert.That(s1.Optional, Is.True, "must never join the identity-validation rule or the certificate DNS gate");

        var s2 = records.Single(r => r.Name == "s2._domainkey");
        Assert.That(s2.Value, Does.StartWith("v=DKIM1; k=rsa; p="));
        Assert.That(s2.Optional, Is.True);
    }
}
