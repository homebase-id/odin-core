using System;
using NUnit.Framework;
using Youverse.Core.Identity;

namespace Youverse.Core.Tests
{
    public class DotYouIdentityTests
    {
        DotYouIdentity frodo = new DotYouIdentity("frodo.digital");
        DotYouIdentity sam = new DotYouIdentity("samwise.digital");
        DotYouIdentity gandalf = new DotYouIdentity("gandalf.MIDDLEEARTH.life"); //Intentionally capitalized

        [SetUp]
        public void Setup()
        {
        }

        [Test(Description = "Can cast to string")]
        public void CanCastToStringTest()
        {
            string gandalf_as_string = (DotYouIdentity) gandalf;

            Assert.IsTrue(gandalf_as_string == gandalf);
            Assert.IsFalse(gandalf_as_string == sam);
        }

        [Test(Description = "Can cast from string")]
        public void CanCastFromStringTest()
        {
            string id = "arwen.middleearth.life";
            DotYouIdentity arwen = (DotYouIdentity) id;
            Assert.IsTrue(string.Equals(id, arwen.ToString(), StringComparison.InvariantCultureIgnoreCase));
        }

        [Test(Description = "Can == two instances")]
        public void CanCompareTwoInstancesEqually()
        {
            var sam2 = new DotYouIdentity("samwise.digital");
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
                DotYouIdentity id = new DotYouIdentity(null); // Null illegal
                bOk = false;
            }
            catch 
            {
                bOk = true;
            }
            Assert.IsTrue(bOk);

            try
            {
                DotYouIdentity id = new DotYouIdentity("ab"); // too short
                bOk = false;
            }
            catch
            {
                bOk = true;
            }
            Assert.IsTrue(bOk);

            try
            {
                DotYouIdentity id = new DotYouIdentity("aba"); // not enough labels
                bOk = false;
            }
            catch
            {
                bOk = true;
            }
            Assert.IsTrue(bOk);

            try
            {
                DotYouIdentity id = new DotYouIdentity("�.a"); // not ASCII
                bOk = false;
            }
            catch
            {
                bOk = true;
            }
            Assert.IsTrue(bOk);
        }

    }
}