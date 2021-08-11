using System;
using DotYou.Types.DataAttribute;

namespace DotYou.Types
{
    public class HumanProfile
    {
        /// <summary>
        /// Specifies the DI address for this Human
        /// </summary>
        public DotYouIdentity Id { get; set; }

        public NameAttribute Name { get; set; }

        /// <summary>
        /// A base64 string of this <see cref="Id"/>'s public key certificate.
        /// </summary>
        public string PublicKeyCertificate { get; set; }
        
        public string AvatarUri { get; set; }
    }
}