using System;
using NUnit.Framework;
using Odin.Core.Identity;
using Odin.Core.Serialization;

namespace Odin.Core.Tests
{
    public class OdinIdTests
    {
        OdinId frodo = new OdinId("frodo.dotyou.cloud");
        OdinId sam = new OdinId("sam.dotyou.cloud");
        OdinId gandalf = new OdinId("gandalf.MIDDLEEARTH.life"); //Intentionally capitalized

        [SetUp]
        public void Setup()
        {
        }

        [Test(Description = "Can cast to string")]
        public void CanCastToStringTest()
        {
            string gandalf_as_string = (OdinId)gandalf;

            Assert.IsTrue(gandalf_as_string == gandalf);
            Assert.IsFalse(gandalf_as_string == sam);
        }

        [Test(Description = "Can cast from string")]
        public void CanCastFromStringTest()
        {
            string id = "arwen.middleearth.life";
            OdinId arwen = (OdinId)id;
            Assert.IsTrue(string.Equals(id, arwen.ToString(), StringComparison.InvariantCultureIgnoreCase));
        }

        [Test(Description = "Can == two instances")]
        public void CanCompareTwoInstancesEqually()
        {
            var sam2 = new OdinId("sam.dotyou.cloud");
            Assert.IsTrue(sam2 == sam);
        }

        [Test(Description = "Can != two instances")]
        public void CanCompareTwoInstancesNotEquals()
        {
            Assert.IsTrue(sam != frodo);
        }

        [Test(Description = "Invalid names rejected")]
        public void InvalidTest()
        {
            bool bOk;
            try
            {
                OdinId id = new OdinId(null); // Null illegal
                bOk = false;
            }
            catch
            {
                bOk = true;
            }

            Assert.IsTrue(bOk);

            try
            {
                OdinId id = new OdinId("ab"); // too short
                bOk = false;
            }
            catch
            {
                bOk = true;
            }

            Assert.IsTrue(bOk);

            try
            {
                OdinId id = new OdinId("aba"); // not enough labels
                bOk = false;
            }
            catch
            {
                bOk = true;
            }

            Assert.IsTrue(bOk);

            try
            {
                OdinId id = new OdinId("ᚢᛏᛁᚾ.a"); // not ASCII
                bOk = false;
            }
            catch
            {
                bOk = true;
            }

            Assert.IsTrue(bOk);
        }

        [Test]
        public void CanSerializeAndDeserialize()
        {
            var c1 = new OdinSerializedTestClass()
            {
                OdinIdAsString = "frodo.dotyou.cloud",
                OdinId = frodo
            };

            var json = OdinSystemSerializer.Serialize(c1);
            var c2 = OdinSystemSerializer.Deserialize<OdinSerializedTestClass>(json);

            Assert.IsTrue(c1.OdinId == c2.OdinId);
            Assert.IsTrue(c1.OdinIdAsString == c2.OdinId);
            Assert.IsTrue(c1.OdinId.DomainName == c2.OdinId);
        }

        [Test]
        public void CanEmbedInString()
        {
            var s1 = $"this is a string of odinid: {frodo}";
            var s2 = $"this is a string of odinid: {frodo.DomainName}";
            Assert.IsTrue(s1 == s2);
        }
    }

    internal class OdinSerializedTestClass
    {
        public string OdinIdAsString { get; init; }
        public OdinId OdinId { get; init; }
    }
}