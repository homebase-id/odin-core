using System;
using System.Linq;
using NUnit.Framework;
using Youverse.Core.Cryptography.Crypto;
using Youverse.Core.Cryptography.Data;
using Youverse.Core.Serialization;

namespace Youverse.Core.Services.Tests.TypeTests;

public struct Timestamp
{
    public long EpochTimeUtc { get; set; }

    public TimeZoneInfo Timezone { get; set; }
}

public class SerializationTests
{
    [Test]
    public void CanSerializeUnixTimeUtc()
    {
        var value = UnixTimeUtc.Now();
        var json = DotYouSystemSerializer.Serialize(value);

        var deserializedValue = DotYouSystemSerializer.Deserialize<UnixTimeUtc>(json);

        Assert.IsTrue(value == deserializedValue);
        Assert.IsTrue(value.milliseconds == deserializedValue.milliseconds);
    }

    [Test]
    public void CanSerializeRsaFullKeyListData()
    {
        const int MAX_KEYS = 2; //leave this size 
        var rsaKeyList = RsaKeyListManagement.CreateRsaKeyList(ref RsaKeyListManagement.zeroSensitiveKey, MAX_KEYS);

        var json = DotYouSystemSerializer.Serialize(rsaKeyList);
        var deserializedValue = DotYouSystemSerializer.Deserialize<RsaFullKeyListData>(json);

        Assert.IsNotNull(deserializedValue);
        Assert.IsNotNull(deserializedValue!.ListRSA.FirstOrDefault());
        Assert.IsFalse(deserializedValue.ListRSA.First().IsDead());
        
    }
}