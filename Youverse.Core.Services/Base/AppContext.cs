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
    /// Context about the App making the request.
    /// </summary>
    public class AppContext
    {
        private readonly SensitiveByteArray _clientSharedSecret;
        private readonly string _appId;
        private readonly Guid _appClientId;
        private readonly List<DriveGrant> _driveGrants;
        private readonly Guid? _driveId;
        private readonly SymmetricKeyEncryptedXor _encryptedAppKey;
        private readonly SensitiveByteArray _clientHalfKek;
        private readonly bool _canManageConnections;

        public AppContext(string appId, Guid appClientId, SensitiveByteArray clientSharedSecret, Guid? driveId, SymmetricKeyEncryptedXor encryptedAppKey, SensitiveByteArray clientHalfKek, List<DriveGrant> driveGrants, bool canManageConnections)
        {
            // Guard.Argument(appId, nameof(appId)).NotNull().NotEmpty();
            // Guard.Argument(deviceUid, nameof(deviceUid)).NotNull().NotEmpty();

            this._appId = appId;
            this._clientSharedSecret = clientSharedSecret;
            this._driveId = driveId;
            this._encryptedAppKey = encryptedAppKey;
            this._clientHalfKek = clientHalfKek;
            this._driveGrants = driveGrants;
            _canManageConnections = canManageConnections;
            this._appClientId = appClientId;
        }

        public Guid AppClientId => this._appClientId;

        public string AppId => this._appId;

        /// <summary>
        /// Specifies the drive associated with this app
        /// </summary>
        public Guid? DriveId => this._driveId;

        /// <summary>
        /// Indicates this app can manage connections and requests.
        /// </summary>
        public bool CanManageConnections => _canManageConnections;
        
        /// <summary>
        /// Returns the shared secret between the client app and
        /// the server.  Do not use for permanent storage.  
        /// </summary>
        /// <returns></returns>
        public SensitiveByteArray GetClientSharedSecret()
        {
            return this._clientSharedSecret;
        }

        /// <summary>
        /// Returns the encryption key specific to this app.  This is only available
        /// when the owner is making an HttpRequest.
        /// </summary>
        /// <returns></returns>
        public SensitiveByteArray GetDriveStorageKey(Guid driveId)
        {
            var grant = _driveGrants?.SingleOrDefault(g => g.DriveId == driveId);

            //TODO: this sort of security check feels like it should be in a service..
            if (null == grant)
            {
                throw new YouverseSecurityException($"App {this._appId} does not have access to drive {driveId}");
            }

            var appKey = this._encryptedAppKey.DecryptKey(this._clientHalfKek);
            var storageKey = grant.AppKeyEncryptedStorageKey.DecryptKey(appKey);
            return storageKey;
        }
    }
}