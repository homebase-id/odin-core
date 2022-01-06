using System.Collections.Generic;
using Youverse.Core.Identity;

namespace Youverse.Hosting.Tests
{
    public static class TestIdentities
    {
        public static readonly DotYouIdentity Frodo = (DotYouIdentity) "frodobaggins.me";
        public static readonly DotYouIdentity Samwise = (DotYouIdentity) "samwisegamgee.me";

        public static List<DotYouIdentity> All = new List<DotYouIdentity>()
        {
            Frodo, Samwise
        };
    }
}