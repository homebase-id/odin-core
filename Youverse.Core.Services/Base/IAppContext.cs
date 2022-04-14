using System;
using System.Collections.Generic;
using Youverse.Core.Cryptography;
using Youverse.Core.Services.Authorization.Apps;
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
        Guid? DefaultDriveId { get; }

        /// <summary>
        /// Indicates this app can manage connections and requests.
        /// </summary>
        bool CanManageConnections { get; }

        List<AppDriveGrant> OwnedDrives { get; init; }

        /// <summary>
        /// Gets the drive id which matches the public drive identifier
        /// </summary>
        /// <returns></returns>
        Guid GetDriveId(Guid driveAlias, bool failIfInvalid = true);

        /// <summary>
        /// Returns the public drive identifier for the given DriveId
        /// </summary>
        /// <param name="driveId"></param>
        /// <returns></returns>
        Guid GetDriveAlias(Guid driveId);

        /// <summary>
        /// Maps an internal file to an external file identifier
        /// </summary>
        /// <param name="file"></param>
        /// <returns></returns>
        ExternalFileIdentifier GetExternalFileIdentifier(InternalDriveFileId file);

        /// <summary>
        /// Returns the shared secret between the client app and
        /// the server.  Do not use for permanent storage.  
        /// </summary>
        /// <value></value>
        SensitiveByteArray ClientSharedSecret { get; }

        SensitiveByteArray GetAppKey();
    }
}