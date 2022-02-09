using System;
using Youverse.Core.Exceptions;
using Youverse.Core.Identity;
using Youverse.Core.Services.Drive;
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

        public DotYouContext(DotYouIdentity hostDotYouId, TenantStorageConfig storageConfig, CallerContext caller, AppContext app)
        {
            this.HostDotYouId = hostDotYouId;
            this.StorageConfig = storageConfig;
            this.Caller = caller;
            this.AppContext = app;
        }

        /// <summary>
        /// Specifies the identifier for this account
        /// </summary>
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

        public CallerContext Caller { get; set; }

        public AppContext AppContext { get; set; }

        public TransitContext TransitContext { get; set; }

        public void AssertCanManageConnections()
        {
            if (AppContext == null)
            {
                TransitContext.AssertCanManageConnections();
                return;
            }

            AppContext.AssertCanManageConnections();
        }

        /// <summary>
        /// Determines if the current request can write to the specified drive
        /// </summary>
        public void AssertCanWriteToDrive(Guid driveId)
        {
            if (AppContext == null || !AppContext.HasDrivePermission(driveId, DrivePermissions.ReadWrite))
            {
                throw new YouverseSecurityException($"Unauthorized to write to drive [{driveId}]");
            }
        }
        
        /// <summary>
        /// Determines if the current request can write to the specified drive
        /// </summary>
        public void AssertCanReadDrive(Guid driveId)
        {
            if (AppContext == null || !AppContext.HasDrivePermission(driveId, DrivePermissions.Read))
            {
                throw new YouverseSecurityException($"Unauthorized to write to drive [{driveId}]");
            }
        }
    }
}