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
        public AppContext(Guid appId, string appName, SensitiveByteArray clientSharedSecret) : base(appId, appName, clientSharedSecret)
        {
        }

        public override SensitiveByteArray GetAppKey()
        {
            throw new Exception("how to have an app key on transit context?  this is required for decrypting the rsa private key");
        }
    }
}