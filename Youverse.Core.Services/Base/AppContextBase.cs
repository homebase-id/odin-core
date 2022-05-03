using System;
using System.Collections.Generic;
using Youverse.Core.Cryptography;
using Youverse.Core.Cryptography.Data;
using Youverse.Core.Exceptions;
using Youverse.Core.Services.Drive;

namespace Youverse.Core.Services.Base
{
    public class AppContextBase : IAppContext
    {
        public AppContextBase(Guid appId, string appName, SensitiveByteArray clientSharedSecret)
        {
            // Guard.Argument(appId, nameof(appId)).NotNull().NotEmpty();
            // Guard.Argument(deviceUid, nameof(deviceUid)).NotNull().NotEmpty();
            this.AppId = appId;
            this.AppName = appName;
            this.ClientSharedSecret = clientSharedSecret;
        }

        public Guid AppId { get; init; }

        public string AppName { get; init; }

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