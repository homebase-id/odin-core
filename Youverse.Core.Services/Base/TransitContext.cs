using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Dawn;
using Youverse.Core.Cryptography;
using Youverse.Core.Cryptography.Data;
using Youverse.Core.Exceptions;
using Youverse.Core.Services.Authorization.Apps;

namespace Youverse.Core.Services.Base
{
    /// <summary>
    /// Context about the App making the request from another DI using the transit protocol
    /// </summary>
    public class TransitContext
    {
        private readonly string _appId;
        private readonly Guid? _driveId;
        private readonly SymmetricKeyEncryptedXor _encryptedAppKey;

        public TransitContext(string appId, Guid? driveId, SymmetricKeyEncryptedXor encryptedAppKey)
        {
            // Guard.Argument(appId, nameof(appId)).NotNull().NotEmpty();
            // Guard.Argument(deviceUid, nameof(deviceUid)).NotNull().NotEmpty();

            this._appId = appId;
            this._driveId = driveId;
            this._encryptedAppKey = encryptedAppKey;
        }

        public string AppId => this._appId;

        /// <summary>
        /// Specifies the drive associated with this app
        /// </summary>
        public Guid? DriveId => this._driveId;
        
        /// <summary>
        /// Returns the encryption key specific to this app.  This is only available
        /// when the owner is making an HttpRequest.
        /// </summary>
        /// <returns></returns>
        public SensitiveByteArray GetDriveStorageKey(Guid driveId)
        {
            return null;
        }
    }
}