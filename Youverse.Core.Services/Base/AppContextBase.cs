using System;
using System.Collections.Generic;
using System.Linq;
using Youverse.Core.Cryptography;
using Youverse.Core.Cryptography.Data;
using Youverse.Core.Exceptions;
using Youverse.Core.Services.Authorization.Apps;
using Youverse.Core.Services.Drive;

namespace Youverse.Core.Services.Base
{
    public class AppContextBase : IAppContext
    {
        public AppContextBase(Guid appId, Guid appClientId, SensitiveByteArray clientSharedSecret, Guid? driveId, List<DriveGrant> driveGrants, bool canManageConnections, SymmetricKeyEncryptedAes masterKeyEncryptedAppKey)
        {
            // Guard.Argument(appId, nameof(appId)).NotNull().NotEmpty();
            // Guard.Argument(deviceUid, nameof(deviceUid)).NotNull().NotEmpty();
            this.AppId = appId;
            this.DriveId = driveId;
            this.DriveGrants = driveGrants;
            this.CanManageConnections = canManageConnections;
            this.AppClientId = appClientId;
            this.ClientSharedSecret = clientSharedSecret;
            this.MasterKeyEncryptedAppKey = masterKeyEncryptedAppKey;
        }

        public Guid AppId { get; init; }

        public Guid AppClientId { get; init; }

        /// <summary>
        /// Specifies the drive associated with this app
        /// </summary>
        public Guid? DriveId { get; init; }

        public List<DriveGrant> DriveGrants { get; init; }

        /// <summary>
        /// Returns the shared secret between the client app and
        /// the server.  Do not use for permanent storage.  
        /// </summary>
        /// <value></value>
        public SensitiveByteArray ClientSharedSecret { get; init; }

        /// <summary>
        /// Indicates this app can manage connections and requests.
        /// </summary>
        public bool CanManageConnections { get; init; }

        public SymmetricKeyEncryptedAes MasterKeyEncryptedAppKey { get; init; }

        public bool HasDrivePermission(Guid driveId, DrivePermissions permission)
        {
            var grant = DriveGrants?.SingleOrDefault(g => g.DriveId == driveId);
            return grant != null && grant.Permissions.HasFlag(permission);
        }

        public void AssertCanManageConnections()
        {
            if (!CanManageConnections)
            {
                throw new YouverseSecurityException("Unauthorized Action");
            }
        }

        /// <summary>
        /// Determines if the current request can write to the specified drive
        /// </summary>
        public void AssertCanWriteToDrive(Guid driveId)
        {
            if (!this.HasDrivePermission(driveId, DrivePermissions.Write))
            {
                throw new YouverseSecurityException($"Unauthorized to write to drive [{driveId}]");
            }
        }

        /// <summary>
        /// Determines if the current request can write to the specified drive
        /// </summary>
        public void AssertCanReadDrive(Guid driveId)
        {
            if (!this.HasDrivePermission(driveId, DrivePermissions.Read))
            {
                throw new YouverseSecurityException($"Unauthorized to write to drive [{driveId}]");
            }
        }

        public virtual SensitiveByteArray GetAppKey()
        {
            throw new NotImplementedException();
        }

        public SensitiveByteArray GetDriveStorageKey(Guid driveId)
        {
            var grant = DriveGrants?.SingleOrDefault(g => g.DriveId == driveId);

            //TODO: this sort of security check feels like it should be in a service..
            if (null == grant)
            {
                throw new YouverseSecurityException($"App {this.AppId} does not have access to drive {driveId}");
            }

            var appKey = this.GetAppKey();
            var storageKey = grant.AppKeyEncryptedStorageKey.DecryptKeyClone(ref appKey);
            appKey.Wipe();
            return storageKey;
        }
    }
}