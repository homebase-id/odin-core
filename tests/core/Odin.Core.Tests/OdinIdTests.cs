using System;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Odin.Core.Identity;

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
            string gandalf_as_string = (OdinId) gandalf;

            ClassicAssert.IsTrue(gandalf_as_string == gandalf);
            ClassicAssert.IsFalse(gandalf_as_string == sam);
        }

        [Test(Description = "Can cast from string")]
        public void CanCastFromStringTest()
        {
            string id = "arwen.middleearth.life";
            OdinId arwen = (OdinId) id;
            ClassicAssert.IsTrue(string.Equals(id, arwen.ToString(), StringComparison.InvariantCultureIgnoreCase));
        }

        [Test(Description = "Can == two instances")]
        public void CanCompareTwoInstancesEqually()
        {
            var sam2 = new OdinId("sam.dotyou.cloud");
            ClassicAssert.IsTrue(sam2 == sam);
        }

        [Test(Description = "Can != two instances")]
        public void CanCompareTwoInstancesNotEquals()
        {
            ClassicAssert.IsTrue(sam != frodo);
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
            ClassicAssert.IsTrue(bOk);

            try
            {
                OdinId id = new OdinId("ab"); // too short
                bOk = false;
            }
            catch
            {
                bOk = true;
            }
            ClassicAssert.IsTrue(bOk);

            try
            {
                OdinId id = new OdinId("aba"); // not enough labels
                bOk = false;
            }
            catch
            {
                bOk = true;
            }
            ClassicAssert.IsTrue(bOk);

            try
            {
                OdinId id = new OdinId("ᚢᛏᛁᚾ.a"); // not ASCII
                bOk = false;
            }
            catch
            {
                bOk = true;
            }
            ClassicAssert.IsTrue(bOk);
        }

    }
}