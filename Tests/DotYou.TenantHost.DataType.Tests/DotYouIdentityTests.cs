using NUnit.Framework;
using System;

namespace DotYou.Types.Tests
{
    public class DotYouIdentityTests
    {
        DotYouIdentity frodo = new DotYouIdentity("frodobaggins.me");
        DotYouIdentity sam = new DotYouIdentity("samwisegamgee.me");
        DotYouIdentity gandalf = new DotYouIdentity("gandalf.MIDDLEEARTH.life"); //Intionally capitalized

        [SetUp]
        public void Setup()
        {
        }

        [Test(Description = "Can cast to string")]
        public void CanCastToStringTest()
        {
            string gandalf_as_string = (DotYouIdentity)gandalf;

            Assert.IsTrue(gandalf_as_string == gandalf);
            Assert.IsFalse(gandalf_as_string == sam);
        }


        [Test(Description = "Can cast from string")]
        public void CanCastFromStringTest()
        {
            string id = "arwen.middleearth.life";
            DotYouIdentity arwen = (DotYouIdentity)id;
            Assert.IsTrue(string.Equals(id, arwen.ToString(), StringComparison.InvariantCultureIgnoreCase));
        }
        [Test(Description = "Can == two instances")]
        public void CanCompareTwoInstancesEqually()
        {
            var sam2 = new DotYouIdentity("samwisegamgee.me");
            Assert.IsTrue(sam2 == sam);
        }


        [Test(Description = "Can != two instances")]
        public void CanCompareTwoInstancesNotEquals()
        {
            Assert.IsTrue(sam != frodo);
        }
    }
}