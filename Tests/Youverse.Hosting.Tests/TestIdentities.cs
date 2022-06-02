using System.Collections.Generic;
using Youverse.Core.Identity;

namespace Youverse.Hosting.Tests
{
    public static class TestIdentities
    {
        public static readonly DotYouIdentity Frodo = (DotYouIdentity) "frodo.digital";
        public static readonly DotYouIdentity Samwise = (DotYouIdentity) "samwise.digital";

        public static List<DotYouIdentity> All = new List<DotYouIdentity>()
        {
            Frodo, Samwise
        };
    }
}