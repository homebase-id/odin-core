using System;
using Youverse.Core.Identity;
using Youverse.Core.Services.Registry;

namespace Youverse.Core.Services.Base
{
    public class TenantContext
    {
        public Guid DotYouRegistryId { get; set; }

        /// <summary>
        /// Specifies the DotYouId of the host
        /// </summary>
        public DotYouIdentity HostDotYouId { get; set; }

        /// <summary>
        /// The root path for data
        /// </summary>
        public string DataRoot { get; set; }

        /// <summary>
        /// The root path for temp data
        /// </summary>
        public string TempDataRoot { get; set; }

        /// <summary>
        /// Specifies the storage locations for various pieces of data for this <see cref="HostDotYouId"/>.
        /// </summary>
        public TenantStorageConfig StorageConfig { get; set; }
    }
}