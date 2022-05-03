using System.Collections.Generic;

namespace Youverse.Core.Services.Authorization.Permissions
{
    public class PermissionSet
    {
        public PermissionSet()
        {
        }

        public Dictionary<SystemApiPermissionType, int> Permissions { get; } = new Dictionary<SystemApiPermissionType, int>();
    }

    public enum SystemApiPermissionType
    {
        Contact = 1,

        CircleNetwork = 2,

        CircleNetworkRequests = 3
    }
}