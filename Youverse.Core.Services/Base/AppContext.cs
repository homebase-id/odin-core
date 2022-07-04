using System;

namespace Youverse.Core.Services.Base
{
    /// <summary>
    /// Context about the App making the request.
    /// </summary>
    public class AppContext : AppContextBase
    {
        public AppContext(Guid appId, string appName) : base(appId, appName)
        {
        }
    }
}