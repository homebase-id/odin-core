using System;
using Dawn;
using DotYou.Kernel.Services.Identity;
using DotYou.Types;
using Identity.DataType.Attributes;

namespace DotYou.Kernel
{
    /// <summary>
    /// Contains all information required to execute commands in the DotYou.Kernel services.
    /// </summary>
    public class DotYouContext
    {
        public DotYouContext(DotYouIdentity dotYouId, IdentityCertificate tenantCertificate, TenantStorageConfig storageConfig)
        {
            Guard.Argument(tenantCertificate, nameof(tenantCertificate)).NotNull();
            Guard.Argument(storageConfig, nameof(storageConfig)).NotNull();
            
            this.DotYouId = dotYouId;
            this.StorageConfig = storageConfig;
            this.TenantCertificate = tenantCertificate;
        }

        /// <summary>
        /// Specifies the Identity of the individual for this context instance.
        /// </summary>
        public DotYouIdentity DotYouId { get; }

        /// <summary>
        /// Specifies the Certficate of the individual for this context instance.
        /// </summary>
        public IdentityCertificate TenantCertificate { get; }

        /// <summary>
        /// Specifies the storage locations for various pieces of data for this <see cref="DotYouId"/>.
        /// </summary>
        public TenantStorageConfig StorageConfig { get; }

    }
}
