using System.Collections.Generic;
using Youverse.Core.Services.Base;

namespace Youverse.Core.Services.Authorization.Permissions
{
    public class PermissionSet
    {
        public PermissionSet()
        {
        }

        public Dictionary<SystemApi, int> Permissions { get; } = new Dictionary<SystemApi, int>();
        
    }
}