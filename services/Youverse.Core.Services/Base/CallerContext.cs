using System;
using System.Collections.Generic;
using Youverse.Core.Cryptography;
using Youverse.Core.Exceptions;
using Youverse.Core.Identity;
using Youverse.Core.Services.Authorization.Acl;
using Youverse.Core.Services.Authorization.ExchangeGrants;

namespace Youverse.Core.Services.Base
{
    /// <summary>
    /// Contains information about the DotYouId calling a given service
    /// </summary>
    public class CallerContext
    {
        private readonly SensitiveByteArray _masterKey;

        public CallerContext(OdinId dotYouId, SensitiveByteArray masterKey, SecurityGroupType securityLevel, List<GuidId> circleIds = null, ClientTokenType tokenType = ClientTokenType.Other)
        {
            this.DotYouId = dotYouId;
            this._masterKey = masterKey;
            this.SecurityLevel = securityLevel;
            this.Circles = circleIds ?? new List<GuidId>();
            this.ClientTokenType = tokenType;
        }

        /// <summary>
        /// The level of access assigned to this caller
        /// </summary>
        public SecurityGroupType SecurityLevel { get; set; }

        public ClientTokenType ClientTokenType { get; set; } = ClientTokenType.Other;

        public IEnumerable<GuidId> Circles { get; set; }

        /// <summary>
        /// Specifies the <see cref="OdinId"/> of the individual calling the API
        /// </summary>
        public OdinId DotYouId { get; }

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

        // public void SetIsConnected()
        // {
        //     //HACK: this method lsets me set isconnected after I've set the dotyoucaller context since it is needed by the CircleNetworkService
        //     this.SecurityLevel = SecurityGroupType.Connected;
        // }

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