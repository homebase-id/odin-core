using Youverse.Core.Cryptography;
using Youverse.Core.Identity;

namespace Youverse.Core.Services.Base
{
    /// <summary>
    /// Contains information about the DotYouId calling a given service
    /// </summary>
    public class CallerContext
    {
        private readonly SecureKey _masterKey;

        public CallerContext(DotYouIdentity dotYouId, bool isOwner, SecureKey masterKey)
        {
            this.DotYouId = dotYouId;
            this.IsOwner = isOwner;
            this._masterKey = masterKey;
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
        public SecureKey GetMasterKey()
        {
            //TODO: add audit point
            return this._masterKey;
        }
    }
}