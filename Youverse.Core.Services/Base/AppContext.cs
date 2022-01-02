using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Dawn;
using Youverse.Core.Cryptography;
using Youverse.Core.Exceptions;

namespace Youverse.Core.Services.Base
{
    /// <summary>
    /// Context about the App making the request.
    /// </summary>
    public class AppContext
    {
        private readonly SensitiveByteArray _deviceSharedSecret;
        private readonly string _appId;
        private readonly byte[] _deviceUid;
        private readonly Dictionary<Guid, SensitiveByteArray> _driveGrants;
        private readonly Guid? _driveId;

        public AppContext(string appId, byte[] deviceUid, SensitiveByteArray deviceSharedSecret, Guid? driveId, Dictionary<Guid, SensitiveByteArray> driveGrants)
        {
            // Guard.Argument(appId, nameof(appId)).NotNull().NotEmpty();
            // Guard.Argument(deviceUid, nameof(deviceUid)).NotNull().NotEmpty();

            this._appId = appId;
            this._deviceSharedSecret = deviceSharedSecret;
            this._driveId = driveId;
            _driveGrants = driveGrants;
            this._deviceUid = deviceUid;
        }

        public byte[] DeviceUid => this._deviceUid;

        public string AppId => this._appId;

        /// <summary>
        /// Specifies the drive associated with this app
        /// </summary>
        public Guid? DriveId => this._driveId;

        /// <summary>
        /// Returns the shared secret between the client app and
        /// the server.  Do not use for permanent storage.  
        /// </summary>
        /// <returns></returns>
        public SensitiveByteArray GetDeviceSharedSecret()
        {
            return this._deviceSharedSecret;
        }

        /// <summary>
        /// Returns the encryption key specific to this app.  This is only available
        /// when the owner is making an HttpRequest.
        /// </summary>
        /// <returns></returns>
        public SensitiveByteArray GetDriveStorageDek(Guid driveId)
        {
            //TODO: this sort of security check feels like it should be in a service..
            if (!this._driveGrants.TryGetValue(driveId, out var dek))
            {
                throw new YouverseSecurityException($"App {this._appId} does not have access to drive {driveId}");
            }

            return dek;
        }
    }
}