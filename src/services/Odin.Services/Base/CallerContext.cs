using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Odin.Core;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Core.Serialization;
using Odin.Services.Authorization.Acl;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Membership.Circles;

namespace Odin.Services.Base
{
    /// <summary>
    /// Contains information about the OdinId calling a given service
    /// </summary>
    [DebuggerDisplay("Caller={OdinId} Security Level={SecurityLevel}")]
    public class CallerContext : IGenericCloneable<CallerContext>
    {
        private readonly SensitiveByteArray _masterKey;

        /// <summary>
        /// The level of access assigned to this caller
        /// </summary>
        public SecurityGroupType SecurityLevel { get; set; }

        public ClientTokenType ClientTokenType { get; set; }
        public IEnumerable<GuidId> Circles { get; set; }

        /// <summary>
        /// Specifies the <see cref="Odin.Core.Identity.OdinId"/> of the individual calling the API
        /// </summary>
        public OdinId? OdinId { get; }

        public OdinClientContext OdinClientContext { get; init; }

        public CallerContext(OdinId? odinId,
            SensitiveByteArray masterKey,
            SecurityGroupType securityLevel,
            OdinClientContext odinClientContext = null,
            List<GuidId> circleIds = null,
            ClientTokenType tokenType = ClientTokenType.Other)
        {
            this.OdinId = odinId;
            this._masterKey = masterKey;
            this.SecurityLevel = securityLevel;
            this.Circles = circleIds ?? new List<GuidId>();
            this.ClientTokenType = tokenType;
            this.OdinClientContext = odinClientContext;
        }

        public CallerContext(CallerContext other)
        {
            this.OdinId = other.OdinId?.Clone();
            this._masterKey = other._masterKey?.Clone();
            this.SecurityLevel = other.SecurityLevel;
            this.Circles = other.Circles?.ToList();
            this.ClientTokenType = other.ClientTokenType;
            this.OdinClientContext = other.OdinClientContext?.Clone();
        }

        public CallerContext Clone()
        {
            return new CallerContext(this);
        }

        public bool HasMasterKey => this._masterKey != null && !this._masterKey.IsEmpty();

        /// <summary>
        /// Specifies if the caller to the service is the owner of the OdinId being acted upon.
        /// </summary>
        public bool IsOwner => this.SecurityLevel == SecurityGroupType.Owner;
        public bool IsSystem => this.SecurityLevel == SecurityGroupType.System;

        public bool IsAnonymous => this.SecurityLevel == SecurityGroupType.Anonymous;

        public bool IsConnected => this.SecurityLevel == SecurityGroupType.Connected;
        public bool IsAuthenticated => this.SecurityLevel == SecurityGroupType.Authenticated;

        public void AssertHasMasterKey()
        {
            if (!HasMasterKey)
            {
                throw new OdinSecurityException("Master key not available; check your auth scheme");
            }
        }
        public void AssertHasMasterKey(out SensitiveByteArray masterKey)
        {
            if (!HasMasterKey)
            {
                throw new OdinSecurityException("Master key not available; check your auth scheme");
            }
            
            masterKey = this._masterKey;
        }

        public void AssertCallerIsOwner()
        {
            if (!IsOwner)
            {
                throw new OdinSecurityException("Caller must be owner");
            }
        }
        
        public void AssertCallerIsOwnerOrSystem()
        {
            if (!IsOwner && !IsSystem)
            {
                throw new OdinSecurityException("Caller must be owner or system");
            }
        }

        public void AssertCallerIsConnected()
        {
            if (!IsConnected)
            {
                throw new OdinSecurityException("Caller must be connected");
            }
        }

        public void AssertCallerIsAuthenticated()
        {
            if ((int)this.SecurityLevel < (int)SecurityGroupType.Authenticated)
            {
                throw new OdinSecurityException("Caller must be authenticated");
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
                IsGrantedConnectedIdentitiesSystemCircle = this.Circles.Any(c => c == SystemCircleConstants.ConfirmedConnectionsCircleId),
                SecurityLevel = this.SecurityLevel,
            };
        }
    }

    public class RedactedCallerContext
    {
        public OdinId? OdinId { get; init; }
        public SecurityGroupType SecurityLevel { get; init; }
        public bool IsGrantedConnectedIdentitiesSystemCircle { get; set; }
    }
}