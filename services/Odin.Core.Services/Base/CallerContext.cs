using System;
using System.Collections.Generic;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Core.Services.Authorization.Acl;
using Odin.Core.Services.Authorization.ExchangeGrants;

namespace Odin.Core.Services.Base
{
    /// <summary>
    /// Contains information about the OdinId calling a given service
    /// </summary>
    public class CallerContext
    {
        private readonly SensitiveByteArray _masterKey;

        public CallerContext(OdinId? odinId, SensitiveByteArray masterKey, SecurityGroupType securityLevel, OdinYouAuthContext youAuthContext = null,
            List<GuidId> circleIds = null,
            ClientTokenType tokenType = ClientTokenType.Other)
        {
            this.OdinId = odinId;
            this._masterKey = masterKey;
            this.SecurityLevel = securityLevel;
            this.Circles = circleIds ?? new List<GuidId>();
            this.ClientTokenType = tokenType;
            this.YouAuthContext = youAuthContext;
        }

        /// <summary>
        /// The level of access assigned to this caller
        /// </summary>
        public SecurityGroupType SecurityLevel { get; set; }

        public ClientTokenType ClientTokenType { get; set; } = ClientTokenType.Other;

        public IEnumerable<GuidId> Circles { get; set; }

        /// <summary>
        /// Specifies the <see cref="Odin.Core.Identity.OdinId"/> of the individual calling the API
        /// </summary>
        public OdinId? OdinId { get; }

        public OdinYouAuthContext YouAuthContext { get; init; }

        public bool HasMasterKey
        {
            get => this._masterKey != null && !this._masterKey.IsEmpty();
        }

        /// <summary>
        /// Specifies if the caller to the service is the owner of the OdinId being acted upon.
        /// </summary>
        public bool IsOwner => this.SecurityLevel == SecurityGroupType.Owner;

        // public bool IsInOdinNetwork => (int)this.SecurityLevel >= (int)SecurityGroupType.Authenticated;
        public bool IsInOdinNetwork => throw new NotImplementedException("need to determine what in network actually means now that we have third party domains");
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
                throw new OdinSecurityException("Master key not available; check your auth scheme");
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

        public RedactedCallerContext Redacted()
        {
            return new RedactedCallerContext()
            {
                OdinId = this.OdinId,
                SecurityLevel = this.SecurityLevel,
            };
        }
    }

    public class RedactedCallerContext
    {
        public OdinId? OdinId { get; init; }
        public SecurityGroupType SecurityLevel { get; init; }
    }
}