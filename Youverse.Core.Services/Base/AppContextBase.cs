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
        public AppContextBase(Guid appId, Guid appClientId, SensitiveByteArray clientSharedSecret, Guid? defaultDriveId, List<AppDriveGrant> ownedDrives, bool canManageConnections, SymmetricKeyEncryptedAes masterKeyEncryptedAppKey)
        {
            // Guard.Argument(appId, nameof(appId)).NotNull().NotEmpty();
            // Guard.Argument(deviceUid, nameof(deviceUid)).NotNull().NotEmpty();
            this.AppId = appId;
            this.DefaultDriveId = defaultDriveId;
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
        public Guid? DefaultDriveId { get; init; }

        public bool CanManageConnections { get; }

        public List<AppDriveGrant> OwnedDrives { get; init; }

        public Guid GetDriveId(Guid driveAlias, bool failIfInvalid = true)
        {
            var driveId = this.OwnedDrives
                .SingleOrDefault(x => x.DriveAlias == driveAlias)?
                .DriveId;
            
            if (!driveId.HasValue && failIfInvalid)
            {
                throw new MissingDataException("Invalid drive alias");
            }
            
            return driveId.GetValueOrDefault();
        }
        
        public Guid GetDriveAlias(Guid driveId)
        {
            var driveAlias = this.OwnedDrives
                .SingleOrDefault(x => x.DriveId == driveId)?
                .DriveAlias;
            
            if (!driveAlias.HasValue)
            {
                throw new MissingDataException("No drive alias found for specified driveId");
            }
            
            return driveAlias.Value;
        }
        
        public ExternalFileIdentifier GetExternalFileIdentifier(InternalDriveFileId file)
        {
            var driveAlias = this.GetDriveAlias(file.DriveId);
            return new ExternalFileIdentifier()
            {
                DriveAlias = driveAlias,
                FileId = file.FileId
            };
        }

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