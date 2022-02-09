using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Dawn;
using Youverse.Core.Cryptography;
using Youverse.Core.Cryptography.Data;
using Youverse.Core.Exceptions;
using Youverse.Core.Services.Authorization.Apps;
using Youverse.Core.Services.Drive;

namespace Youverse.Core.Services.Base
{
    /// <summary>
    /// Context about the App making the request.
    /// </summary>
    public class AppContext
    {
        private readonly SensitiveByteArray _clientSharedSecret;
        private readonly Guid _appId;
        private readonly Guid _appClientId;
        private readonly List<DriveGrant> _driveGrants;
        private readonly Guid? _driveId;
        private readonly SymmetricKeyEncryptedXor _encryptedAppKey;
        private SensitiveByteArray _clientHalfKek; //TODO: can we make this readonly?
        private readonly bool _canManageConnections;

        public AppContext(Guid appId, Guid appClientId, SensitiveByteArray clientSharedSecret, Guid? driveId, SymmetricKeyEncryptedXor encryptedAppKey, SensitiveByteArray clientHalfKek, List<DriveGrant> driveGrants, bool canManageConnections)
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

        public Guid AppId => this._appId;

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

            if (null == grant)
            {
                throw new YouverseSecurityException($"App {this._appId} does not have access to drive {driveId}");
            }

            var appKey = this.GetAppKey();
            var storageKey = grant.AppKeyEncryptedStorageKey.DecryptKeyClone(ref appKey);
            return storageKey;
        }

        public bool HasDrivePermission(Guid driveId, DrivePermissions permission)
        {
            var grant = _driveGrants?.SingleOrDefault(g => g.DriveId == driveId);
            return grant != null && grant.Permissions.HasFlag(permission);
        }

        public SensitiveByteArray GetAppKey()
        {
            var appKey = this._encryptedAppKey.DecryptKeyClone(ref this._clientHalfKek);
            return appKey;
        }

        public void AssertCanManageConnections()
        {
            if (!_canManageConnections)
            {
                throw new YouverseSecurityException("Unauthorized Action");
            }
        }
    }
}