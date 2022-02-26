using System;
using System.Collections.Generic;
using Youverse.Core.Cryptography;
using Youverse.Core.Cryptography.Data;
using Youverse.Core.Services.Authorization.Apps;

namespace Youverse.Core.Services.Base
{
    public class AppContextBase : IAppContext
    {
        public AppContextBase(Guid appId, Guid appClientId, SensitiveByteArray clientSharedSecret, Guid? driveId, List<AppDriveGrant> ownedDrives, bool canManageConnections, SymmetricKeyEncryptedAes masterKeyEncryptedAppKey)
        {
            // Guard.Argument(appId, nameof(appId)).NotNull().NotEmpty();
            // Guard.Argument(deviceUid, nameof(deviceUid)).NotNull().NotEmpty();
            this.AppId = appId;
            this.DriveId = driveId;
            this.OwnedDrives = ownedDrives;
            this.AppClientId = appClientId;
            this.ClientSharedSecret = clientSharedSecret;
            this.MasterKeyEncryptedAppKey = masterKeyEncryptedAppKey;
            this.CanManageConnections = canManageConnections;
        }

        public Guid AppId { get; init; }

        public Guid AppClientId { get; init; }

        /// <summary>
        /// Specifies the drive associated with this app
        /// </summary>
        public Guid? DriveId { get; init; }

        public bool CanManageConnections { get; }

        public List<AppDriveGrant> OwnedDrives { get; init; }

        /// <summary>
        /// Returns the shared secret between the client app and
        /// the server.  Do not use for permanent storage.  
        /// </summary>
        /// <value></value>
        public SensitiveByteArray ClientSharedSecret { get; init; }
        
        public SymmetricKeyEncryptedAes MasterKeyEncryptedAppKey { get; init; }
        
        public virtual SensitiveByteArray GetAppKey()
        {
            return null;
        }

    }
}