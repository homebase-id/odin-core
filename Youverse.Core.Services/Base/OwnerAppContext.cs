using System;
using System.Collections.Generic;
using Youverse.Core.Cryptography;
using Youverse.Core.Cryptography.Data;
using Youverse.Core.Services.Authorization.Apps;

namespace Youverse.Core.Services.Base
{
    public class OwnerAppContext : AppContextBase
    {
        private readonly SensitiveByteArray _masterKey;

        public OwnerAppContext(Guid appId, string appName, SensitiveByteArray masterKey, SensitiveByteArray clientSharedSecret) : base(appId, appName, clientSharedSecret)
        {
            this._masterKey = masterKey;
        }

        public override SensitiveByteArray GetAppKey()
        {
            var mk = this._masterKey;
            var appKey = this.MasterKeyEncryptedAppKey.DecryptKeyClone(ref mk);
            return appKey;
        }
    }
}