using System;
using Youverse.Core.Cryptography;

namespace Youverse.Core.Services.Base
{
    public class OwnerAppContext : AppContextBase
    {

        public OwnerAppContext(Guid appId, string appName) : base(appId, appName)
        {
        }

    }
}