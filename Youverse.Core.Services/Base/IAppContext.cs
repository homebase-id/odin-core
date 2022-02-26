using System;
using System.Collections.Generic;
using Youverse.Core.Cryptography;
using Youverse.Core.Services.Authorization.Apps;

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

        List<AppDriveGrant> OwnedDrives { get; init; }

        /// <summary>
        /// Gets the drive id which matches the public drive identifier
        /// </summary>
        /// <param name="driveIdentifier"></param>
        /// <returns></returns>
        Guid GetDriveId(Guid driveIdentifier);
        
        /// <summary>
        /// Returns the shared secret between the client app and
        /// the server.  Do not use for permanent storage.  
        /// </summary>
        /// <value></value>
        SensitiveByteArray ClientSharedSecret { get; }

        SensitiveByteArray GetAppKey();
    }
}