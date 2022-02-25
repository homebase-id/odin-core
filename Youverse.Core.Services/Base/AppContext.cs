using System;
using System.Collections.Generic;
using Youverse.Core.Cryptography;
using Youverse.Core.Cryptography.Data;
using Youverse.Core.Services.Authorization.Apps;

namespace Youverse.Core.Services.Base
{
    /// <summary>
    /// Context about the App making the request.
    /// </summary>
    public class AppContext : AppContextBase
    {
        private readonly SymmetricKeyEncryptedXor _hostHalfAppKey;
        private SensitiveByteArray _clientHalfAppKey; //TODO: can we make this readonly?

        public AppContext(Guid appId, Guid appClientId, SensitiveByteArray clientSharedSecret, Guid? driveId, SymmetricKeyEncryptedXor hostHalfAppKey, SensitiveByteArray clientHalfAppKey, List<AppDriveGrant> driveGrants, bool canManageConnections)
            : base(appId, appClientId, clientSharedSecret, driveId, driveGrants, canManageConnections, null)
        {
            this.ClientSharedSecret = clientSharedSecret;
            this._hostHalfAppKey = hostHalfAppKey;
            this._clientHalfAppKey = clientHalfAppKey;
        }
        
        /// <summary>
        /// Returns the encryption key specific to this app.  This is only available
        /// when the owner is making an HttpRequest.
        /// </summary>
        /// <returns></returns>
        public override SensitiveByteArray GetAppKey()
        {
            var appKey = this._hostHalfAppKey.DecryptKeyClone(ref this._clientHalfAppKey);
            return appKey;
        }
    }
}