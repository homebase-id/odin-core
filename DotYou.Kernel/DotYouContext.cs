using System;
using Identity.Web.Certificate;

namespace DotYou.Kernel
{
    public class DotYouContext
    {
        public DotYouContext() {}

        public Guid Id { get; set; }

        public IdentityCertificate Certificate { get; set; }
    }
}
