﻿using Dawn;
using Youverse.Core.Identity;
using Youverse.Core.Services.Identity;
using Youverse.Core.Services.Registry;

namespace Youverse.Core.Services.Base
{
    /// <summary>
    /// Contains all information required to execute commands in the Youverse.Core.Services services.
    /// </summary>
    public class DotYouContext
    {
        public DotYouContext()
        {
            
        }
        
        public DotYouContext(DotYouIdentity hostDotYouId, IdentityCertificate tenantCertificate, TenantStorageConfig storageConfig, CallerContext caller, AppContext app)
        {
            this.HostDotYouId = hostDotYouId;
            this.StorageConfig = storageConfig;
            this.Caller = caller;
            this.AppContext = app;
            this.TenantCertificate = tenantCertificate;
        }

        /// <summary>
        /// Specifies the DotYouId of the host
        /// </summary>
        public DotYouIdentity HostDotYouId { get; set; }

        /// <summary>
        /// Specifies the certificate of the individual for this context instance.
        /// </summary>
        public IdentityCertificate TenantCertificate { get; set; }

        /// <summary>
        /// Specifies the storage locations for various pieces of data for this <see cref="HostDotYouId"/>.
        /// </summary>
        public TenantStorageConfig StorageConfig { get; set; }

        public CallerContext Caller { get; set; }
        
        public AppContext AppContext { get; set; }
    }
}