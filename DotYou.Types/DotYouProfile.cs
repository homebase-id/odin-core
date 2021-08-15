using System;
using DotYou.Types.DataAttribute;

namespace DotYou.Types
{
    /// <summary>
    /// Base class for any which need their ID to be based on the <see cref="DotYouIdentity"/>.  This is useful for storage in LiteDB
    /// </summary>
    public abstract class DotYouIdBase
    {
        //used as the storage Id for LiteDB
        public Guid Id
        {
            get { return this.DotYouId; }
            set
            {
                //no-op
            }
        }

        /// <summary>
        /// Specifies the DI address for this Human
        /// </summary>
        public DotYouIdentity DotYouId { get; init; }
    }

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