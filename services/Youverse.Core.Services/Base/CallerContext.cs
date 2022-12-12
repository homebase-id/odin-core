using System;
using System.Collections.Generic;
using Youverse.Core.Cryptography;
using Youverse.Core.Exceptions;
using Youverse.Core.Identity;
using Youverse.Core.Services.Authorization.Acl;

namespace Youverse.Core.Services.Base
{
    /// <summary>
    /// Contains information about the DotYouId calling a given service
    /// </summary>
    public class CallerContext
    {
        private readonly SensitiveByteArray _masterKey;

        public CallerContext(DotYouIdentity dotYouId, SensitiveByteArray masterKey, SecurityGroupType securityLevel, List<GuidId> circleIds = null)
        {
            this.DotYouId = dotYouId;
            this._masterKey = masterKey;
            this.SecurityLevel = securityLevel;
            this.Circles = circleIds ?? new List<GuidId>();
        }

        /// <summary>
        /// The level of access assigned to this caller
        /// </summary>
        public SecurityGroupType SecurityLevel { get; set; }


        public IEnumerable<GuidId> Circles { get; set; }

        /// <summary>
        /// Specifies the <see cref="DotYouIdentity"/> of the individual calling the API
        /// </summary>
        public DotYouIdentity DotYouId { get; }

        public bool HasMasterKey
        {
            get => this._masterKey != null && !this._masterKey.IsEmpty();
        }

        /// <summary>
        /// Specifies if the caller to the service is the owner of the DotYouId being acted upon.
        /// </summary>
        public bool IsOwner => this.SecurityLevel == SecurityGroupType.Owner;

        public bool IsInYouverseNetwork => (int)this.SecurityLevel >= (int)SecurityGroupType.Authenticated;
        public bool IsAnonymous => this.SecurityLevel == SecurityGroupType.Anonymous;

        public bool IsConnected => this.SecurityLevel == SecurityGroupType.Connected;

        public void SetIsConnected()
        {
            //HACK: this method lsets me set isconnected after I've set the dotyoucaller context since it is needed by the CircleNetworkService
            this.SecurityLevel = SecurityGroupType.Connected;
        }

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