using System;
using DotYou.Kernel.Services.Identity;
using DotYou.Types;

namespace DotYou.Kernel
{
    /// <summary>
    /// Contains all information required to execute commands in the DotYou.Kernel services.
    /// </summary>
    public class DotYouContext
    {
        private readonly TenantStorageConfig _storageConfig;
        private readonly IdentityCertificate _tenantCertificate;
        private readonly DotYouIdentity _dotYouId;
        public DotYouContext(DotYouIdentity dotYouId, IdentityCertificate tenantCertificate, TenantStorageConfig storageConfig)
        {
            this._dotYouId = dotYouId;
            this._storageConfig = storageConfig;
            this._tenantCertificate = tenantCertificate;
        }

        /// <summary>
        /// Specifies the Identity of the individual for this context instance.
        /// </summary>
        public DotYouIdentity DotYouId { get => this._dotYouId; }
        
        /// <summary>
        /// Specifies the Certficate of the individual for this context instance.
        /// </summary>
        public IdentityCertificate TenantCertificate { get => this._tenantCertificate; }

        /// <summary>
        /// Specifies the storage locations for various pieces of data for this <see cref="DotYouId"/>.
        /// </summary>
        public TenantStorageConfig StorageConfig { get => this._storageConfig; }

    }
}
