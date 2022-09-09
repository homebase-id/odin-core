using System;

namespace Youverse.Core.Services.Base
{
    /// <summary>
    /// Context about the App making the request.
    /// </summary>
    public class AppContext : AppContextBase
    {
        public AppContext(ByteArrayId appId, string appName) : base(appId, appName)
        {
        }
    }
}