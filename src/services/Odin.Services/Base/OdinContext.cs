using System;
using System.Diagnostics;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Core.Serialization;
using Odin.Core.Time;
using Odin.Services.Authorization.ExchangeGrants;

namespace Odin.Services.Base
{
    public interface IOdinContext : IGenericCloneable<IOdinContext>
    {
        string AuthContext { get; }
        int ApiVersion { get; }
        OdinId Tenant { get; set; }
        CallerContext Caller { get; set; }
        PermissionContext PermissionsContext { get; }

        /// <summary>
        /// The token of the HTTP request this is the context of, while it is marked as that request's own: what a
        /// drive write made with it is attributed to, for sockets that exclude their own writes. Null for contexts
        /// built for jobs or system work, and for a plain copy (<see cref="IGenericCloneable{T}.Clone"/> does not
        /// carry the mark), so work done later or on someone else's behalf is never taken for the request's own
        /// write; a copy that is the same request with more permissions carries it (see OdinContextUpgrades).
        /// </summary>
        Guid? RequestTokenId { get; }

        /// <summary>
        /// The request's own writes while this is marked as that request's context; null otherwise
        /// </summary>
        RequestOwnWrites OwnWrites { get; }

        /// <summary>
        /// Marks this as the context of the HTTP request being handled (see <see cref="RequestTokenId"/>). A copy that
        /// is the same request passes the original's <paramref name="ownWrites"/>, so both record into one.
        /// </summary>
        void MarkAsRequestContext(RequestOwnWrites ownWrites = null);

        /// <summary>
        /// The age of the <see cref="ClientAuthenticationToken"/>
        /// </summary>
        UnixTimeUtc? AuthTokenCreated { get; set; }

        OdinId GetCallerOdinIdOrFail();
        void SetApiVersion(int version);
        void SetPermissionContext(PermissionContext pc);
        void SetAuthContext(string authContext);
        void AssertCanManageConnections();
        RedactedOdinContext Redacted();
    }

    //
    [DebuggerDisplay("{DebugDisplay}")]
    public class OdinContext : IOdinContext
    {
        public PermissionContext PermissionsContext { get; private set; }
        public string AuthContext { get; private set; }
        public int ApiVersion { get; private set; } = 1;

        public OdinId Tenant { get; set; }
        public UnixTimeUtc? AuthTokenCreated { get; set; }
        public CallerContext Caller { get; set; }

        public RequestOwnWrites OwnWrites { get; private set; }

        public Guid? RequestTokenId => OwnWrites != null ? Caller?.OdinClientContext?.AccessRegistrationId?.Value : null;

        public void MarkAsRequestContext(RequestOwnWrites ownWrites = null) => OwnWrites = ownWrites ?? new RequestOwnWrites();

        public IOdinContext Clone()
        {
            return new OdinContext
            {
                Tenant = Tenant.Clone(),
                Caller = Caller?.Clone(),
                PermissionsContext = PermissionsContext?.Clone(),
                AuthContext = AuthContext,
                ApiVersion = ApiVersion,
                AuthTokenCreated = AuthTokenCreated?.Clone()
            };
        }

        public OdinId GetCallerOdinIdOrFail()
        {
            return Caller.OdinId ?? throw new OdinSystemException("Invalid Caller");
        }

        public void SetPermissionContext(PermissionContext pc)
        {
            //This is only exist to ensure we only set permissions in the DotYouContextMiddleware
            if (null != PermissionsContext)
            {
                throw new OdinSecurityException("Cannot set permission context since it is already set");
            }

            PermissionsContext = pc;
        }

        public void SetAuthContext(string authContext)
        {
            //This is only exist to ensure we only set auth context in the DotYouContextMiddleware
            if (null != AuthContext)
            {
                throw new OdinSecurityException("Cannot reset auth context");
            }

            AuthContext = authContext;
        }

        public void SetApiVersion(int version)
        {
            this.ApiVersion = version;
        }

        public void AssertCanManageConnections()
        {
            if (Caller.IsOwner && Caller.HasMasterKey)
            {
                return;
            }

            throw new OdinSecurityException("Unauthorized Action");
        }


        public RedactedOdinContext Redacted()
        {
            return new RedactedOdinContext
            {
                Caller = Caller.Redacted(),
                PermissionContext = PermissionsContext.Redacted()
            };
        }

        private string DebugDisplay => $"{Caller.OdinId} is calling {Tenant} with security {Caller.SecurityLevel} with client token Type: {Caller.ClientTokenType}";
    }

    public class RedactedOdinContext
    {
        public RedactedCallerContext Caller { get; set; }
        public RedactedPermissionContext PermissionContext { get; set; }
    }
}