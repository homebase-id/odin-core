using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using NUnit.Framework;
using Youverse.Core.Identity;
using Youverse.Core.Services.Transit;
using Youverse.Core.Services.Transit.Upload;

namespace Youverse.Core.Tests
{
    public class DotYouIdentityTests
    {
        DotYouIdentity frodo = new DotYouIdentity("frodobaggins.me");
        DotYouIdentity sam = new DotYouIdentity("samwisegamgee.me");
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
            var sam2 = new DotYouIdentity("samwisegamgee.me");
            Assert.IsTrue(sam2 == sam);
        }

        [Test(Description = "Can != two instances")]
        public void CanCompareTwoInstancesNotEquals()
        {
            Assert.IsTrue(sam != frodo);
        }

        [Test(Description = "")]
        public void CanDeserializeRecipientList()
        {
            var x = new RecipientList();
            x.Recipients = new List<DotYouIdentity>() {(DotYouIdentity) "frodobaggins.me", (DotYouIdentity) "sam.me"};
            var json = JsonConvert.SerializeObject(x);
            Console.WriteLine($"json:{json}");

            var x2 = JsonConvert.DeserializeObject<RecipientList>(json);

            Assert.IsNotNull(x2);
            Assert.IsTrue(x.Recipients.Count == x2.Recipients.Count);

            var x1Ordered = x.Recipients.OrderBy(k => k.ToGuid());
            var x2Ordered = x2.Recipients.OrderBy(k => k.ToGuid());
            
            Assert.IsTrue(x1Ordered.First()== x2Ordered.First());
            Assert.IsTrue(x1Ordered.Last()== x2Ordered.Last());
        }
    }
}