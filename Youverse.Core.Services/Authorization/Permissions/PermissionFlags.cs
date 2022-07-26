using System;

namespace Youverse.Core.Services.Authorization.Permissions
{
    [Flags]
    public enum PermissionFlags
    {
        None = 0,

        ApproveConnection = 1,

        ReadConnections = 2,

        UpdateConnections = 4,

        DeleteConnections = 8,

        ManageAllConnections = ApproveConnection | ReadConnections | UpdateConnections | DeleteConnections,

        CreateOrSendConnectionRequests = 16,

        ReadConnectionRequests = 32,

        DeleteConnectionRequests = 64,

        ManageConnectionRequests = CreateOrSendConnectionRequests | ReadConnectionRequests | DeleteConnectionRequests
    }
}