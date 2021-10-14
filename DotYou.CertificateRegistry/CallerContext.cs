using DotYou.Kernel.Cryptography;
using DotYou.Types;

namespace DotYou.IdentityRegistry
{
    /// <summary>
    /// Contains information about the DotYouId calling a given service
    /// </summary>
    public class CallerContext
    {
        private readonly SecureKey _loginKek;

        public CallerContext(DotYouIdentity dotYouId, bool isOwner, SecureKey loginKek)
        {
            this.DotYouId = dotYouId;
            this.IsOwner = isOwner;
            this._loginKek = loginKek;
        }

        /// <summary>
        /// Specifies the <see cref="DotYouIdentity"/> of the individual calling the API
        /// </summary>
        public DotYouIdentity DotYouId { get; }

        /// <summary>
        /// Specifies if the caller to the service is the owner of the DotYouId being acted upon.
        /// </summary>
        public bool IsOwner { get; }


        /// <summary>
        /// Returns the login kek if the owner is logged; otherwise null
        /// </summary>
        public SecureKey GetLoginKek()
        {
            //TODO: add audit point
            return this._loginKek;
        }

    }
}