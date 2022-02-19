using System;
using Youverse.Core.Cryptography;
using Youverse.Core.Services.Drive;

namespace Youverse.Core.Services.Base
{
    public interface IAppContext
    {
        Guid AppId { get; }

        Guid AppClientId { get; }

        /// <summary>
        /// Specifies the drive associated with this app
        /// </summary>
        Guid? DriveId { get; }

        /// <summary>
        /// Indicates this app can manage connections and requests.
        /// </summary>
        bool CanManageConnections { get; }

        /// <summary>
        /// Returns the shared secret between the client app and
        /// the server.  Do not use for permanent storage.  
        /// </summary>
        /// <value></value>
        SensitiveByteArray ClientSharedSecret { get; }

        /// <summary>
        /// Returns the encryption key specific to this app.  This is only available
        /// when the owner is making an HttpRequest.
        /// </summary>
        /// <returns></returns>
        SensitiveByteArray GetDriveStorageKey(Guid driveId);

        bool HasDrivePermission(Guid driveId, DrivePermissions permission);
        
        SensitiveByteArray GetAppKey();
        
        /// <summary>
        /// Determines if the current request can write to the specified drive
        /// </summary>
        void AssertCanWriteToDrive(Guid driveId);

        /// <summary>
        /// Determines if the current request can write to the specified drive
        /// </summary>
        void AssertCanReadDrive(Guid driveId);
    }
}