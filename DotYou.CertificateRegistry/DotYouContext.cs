using System.Security.Claims;
using Dawn;
using DotYou.Types;

namespace DotYou.IdentityRegistry
{
    /// <summary>
    /// Contains all information required to execute commands in the DotYou.Kernel services.
    /// </summary>
    public class DotYouContext
    {
        public DotYouContext(DotYouIdentity hostDotYouId, IdentityCertificate tenantCertificate, TenantStorageConfig storageConfig, DotYouIdentity callerDotYouId)
        {
            Guard.Argument(tenantCertificate, nameof(tenantCertificate)).NotNull();
            Guard.Argument(storageConfig, nameof(storageConfig)).NotNull();

            this.HostDotYouId = hostDotYouId;
            this.StorageConfig = storageConfig;
            this.TenantCertificate = tenantCertificate;
            this.CallerDotYouId = callerDotYouId;
        }

        /// <summary>
        /// Specifies the DotYouId of the host
        /// </summary>
        public DotYouIdentity HostDotYouId { get; }

        /// <summary>
        /// Specifies the Certficate of the individual for this context instance.
        /// </summary>
        public IdentityCertificate TenantCertificate { get; }

        /// <summary>
        /// Specifies the storage locations for various pieces of data for this <see cref="HostDotYouId"/>.
        /// </summary>
        public TenantStorageConfig StorageConfig { get; }

        /// <summary>
        /// Specifies the <see cref="DotYouIdentity"/> of the individual calling the API
        /// </summary>
        public DotYouIdentity CallerDotYouId { get; }

        /*         
        new Claim(ClaimTypes.NameIdentifier, domain, ClaimValueTypes.String, context.Options.ClaimsIssuer),
        new Claim(ClaimTypes.Name, domain, ClaimValueTypes.String, context.Options.ClaimsIssuer),
        new Claim(DotYouClaimTypes.IsIdentityOwner, isTenantOwner.ToString().ToLower(), ClaimValueTypes.Boolean, YouFoundationIssuer),
        new Claim(DotYouClaimTypes.IsIdentified, isIdentified.ToString().ToLower(), ClaimValueTypes.Boolean, YouFoundationIssuer),
         */
    }
}