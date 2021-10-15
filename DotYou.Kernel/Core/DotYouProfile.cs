using DotYou.Types.DataAttribute;

namespace DotYou.Types
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