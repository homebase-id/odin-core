using System;
using Youverse.Core.Cryptography;

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
        
        SensitiveByteArray GetAppKey();
        
    }
}