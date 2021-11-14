using System;
using Youverse.Core.Cryptography;

namespace Youverse.Core.Services.Base
{
    /// <summary>
    /// Context about the App making the request
    /// </summary>
    public class AppContext
    {
        private readonly SecureKey _appKey;
        public AppContext(SecureKey appKey)
        {
            this._appKey = appKey;
        }

        /// <summary>
        /// Returns the shared secret between the client app and the server
        /// </summary>
        /// <returns></returns>
        public SecureKey GetSharedSecret()
        {
            return this._appKey;
        }
    }
}