using System;
using DotYou.Kernel.Services.Identity;

namespace DotYou.Kernel
{
    /// <summary>
    /// Contains all information required to execute commands in the DotYou.Kernel services.
    /// </summary>
    public class DotYouContext
    {
        public DotYouContext() {}

        /// <summary>
        /// Specifies the Identity of the individual being acted upon.
        /// </summary>
        public IdentityCertificate Certificate { get; set; }
    }
}
