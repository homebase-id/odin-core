using System;
using System.Linq;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Odin.Core.Cryptography.Crypto;
using Odin.Core.Cryptography.Data;
using Odin.Core.Serialization;
using Odin.Core.Time;

namespace Odin.Services.Tests.TypeTests;

public struct Timestamp
{
    public UnixTimeUtc EpochTimeUtc { get; set; }

    public TimeZoneInfo Timezone { get; set; }
}

public class SerializationTests
{
    [Test]
    public void CanSerializeUnixTimeUtc()
    {
        var value = UnixTimeUtc.Now();
        var json = OdinSystemSerializer.Serialize(value);

        var deserializedValue = OdinSystemSerializer.Deserialize<UnixTimeUtc>(json);

        ClassicAssert.IsTrue(value == deserializedValue);
        ClassicAssert.IsTrue(value.milliseconds == deserializedValue.milliseconds);
    }

    [Test]
    public void CanSerializeRsaFullKeyListData()
    {
        const int MAX_KEYS = 2; //leave this size 
        var rsaKeyList = RsaKeyListManagement.CreateRsaKeyList(RsaKeyListManagement.zeroSensitiveKey, MAX_KEYS, RsaKeyListManagement.DefaultHoursOfflineKey);

        var json = OdinSystemSerializer.Serialize(rsaKeyList);
        var deserializedValue = OdinSystemSerializer.Deserialize<RsaFullKeyListData>(json);

        ClassicAssert.IsNotNull(deserializedValue);
        ClassicAssert.IsNotNull(deserializedValue!.ListRSA.FirstOrDefault());
        ClassicAssert.IsFalse(deserializedValue.ListRSA.First().IsDead());
        
    }
}