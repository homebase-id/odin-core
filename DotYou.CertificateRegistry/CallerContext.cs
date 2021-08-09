using DotYou.Types;

namespace DotYou.IdentityRegistry
{
    /// <summary>
    /// Contains information about the DotYouId calling a given service
    /// </summary>
    public class CallerContext
    {
        public CallerContext(DotYouIdentity dotYouId, bool isOwner)
        {
            this.DotYouId = dotYouId;
            IsOwner = isOwner;
        }

        /// <summary>
        /// Specifies the <see cref="DotYouIdentity"/> of the individual calling the API
        /// </summary>
        public DotYouIdentity DotYouId { get; }

        /// <summary>
        /// Specifies if the caller to the service is the owner of the DotYouId being acted upon.
        /// </summary>
        public bool IsOwner { get; }

    }
}