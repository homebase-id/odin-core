using System.Runtime.CompilerServices;
using Dawn;
using Youverse.Core.Cryptography;

namespace Youverse.Core.Services.Base
{
    /// <summary>
    /// Context about the App making the request.
    /// </summary>
    public class AppContext
    {
        private readonly SecureKey _appEncryptionKey;
        private readonly SecureKey _appSharedSecret;
        private readonly string _appId;
        private readonly string _deviceUid;
        private readonly bool _isAdminApp;

        public AppContext(string appId, string deviceUid, SecureKey appEncryptionKey, SecureKey appSharedSecret, bool isAdminApp)
        {
            // Guard.Argument(appId, nameof(appId)).NotNull().NotEmpty();
            // Guard.Argument(deviceUid, nameof(deviceUid)).NotNull().NotEmpty();

            this._appId = appId;
            this._appEncryptionKey = appEncryptionKey;
            this._appSharedSecret = appSharedSecret;
            _isAdminApp = isAdminApp;
            this._deviceUid = deviceUid;
        }

        public string DeviceUid => this._deviceUid;

        public string AppId => this._appId;

        /// <summary>
        /// Returns the shared secret between the client app and
        /// the server.  Do not use for permanent storage.  
        /// </summary>
        /// <returns></returns>
        public SecureKey GetSharedSecret()
        {
            return this._appSharedSecret;
        }

        /// <summary>
        /// Returns the encryption key specific to this app.  This is only available
        /// when the owner is making an HttpRequest.
        /// </summary>
        /// <returns></returns>
        public SecureKey GetAppEncryptionKey()
        {
            return this._appEncryptionKey;
        }

        public bool IsAdminApp()
        {
            return _isAdminApp;
        }
    }
}