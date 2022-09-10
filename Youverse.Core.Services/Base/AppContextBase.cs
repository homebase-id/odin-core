using System;
using Youverse.Core.Cryptography;
using Youverse.Core.Cryptography.Data;

namespace Youverse.Core.Services.Base
{
    public class AppContextBase : IAppContext
    {
        public AppContextBase(ByteArrayId appId, string appName)
        {
            // Guard.Argument(appId, nameof(appId)).NotNull().NotEmpty();
            // Guard.Argument(deviceUid, nameof(deviceUid)).NotNull().NotEmpty();
            this.AppId = appId;
            this.AppName = appName;
        }

        public ByteArrayId AppId { get; init; }

        public string AppName { get; init; }
        
    }
}