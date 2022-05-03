using Youverse.Core.Cryptography;
using Youverse.Core.Exceptions;
using Youverse.Core.Identity;

namespace Youverse.Core.Services.Base
{
    /// <summary>
    /// Contains information about the DotYouId calling a given service
    /// </summary>
    public class CallerContext
    {
        private readonly SensitiveByteArray _masterKey;

        public CallerContext(DotYouIdentity dotYouId, bool isOwner, SensitiveByteArray masterKey, string authContext, bool isInYouverseNetwork = false, bool isAnonymous = true, bool isConnected = false)
        {
            this.DotYouId = dotYouId;
            this.IsOwner = isOwner;
            this._masterKey = masterKey;
            this.AuthContext = authContext;
            this.IsInYouverseNetwork = isInYouverseNetwork;
            this.IsAnonymous = isAnonymous;
            this.IsConnected = isConnected;
        }

        public string AuthContext { get; set; }

        /// <summary>
        /// Specifies the <see cref="DotYouIdentity"/> of the individual calling the API
        /// </summary>
        public DotYouIdentity DotYouId { get; }

        /// <summary>
        /// Specifies if the caller to the service is the owner of the DotYouId being acted upon.
        /// </summary>
        public bool IsOwner { get; }

        public bool HasMasterKey
        {
            get => this._masterKey != null && !this._masterKey.IsEmpty();
        }

        public bool IsInYouverseNetwork { get; }
        public bool IsAnonymous { get; }

        public bool IsConnected { get; }


        public void AssertHasMasterKey()
        {
            if (!HasMasterKey)
            {
                throw new YouverseSecurityException("Master key not available; check your auth scheme");
            }
        }

        /// <summary>
        /// Returns the login kek if the owner is logged; otherwise null
        /// </summary>
        public SensitiveByteArray GetMasterKey()
        {
            AssertHasMasterKey();

            //TODO: add audit point
            return this._masterKey;
        }
    }
}