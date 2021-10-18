using Youverse.Core.Identity.DataAttribute;

namespace Youverse.Core.Identity
{
    public class DotYouProfile : DotYouIdBase
    {
        public NameAttribute Name { get; init; }

        /// <summary>
        /// A base64 string of this <see cref="DotYouId"/>'s public key certificate.
        /// </summary>
        public string PublicKeyCertificate { get; init; }

        public string AvatarUri { get; init; }
    }
}