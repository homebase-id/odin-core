using System;

namespace Youverse.Core.Services.Authorization.Permissions
{
    //TODO: need to breakout permissions that are only valid when the master key is present versus those which can be done by an app or youauth
    [Flags]
    public enum PermissionFlags
    {
        None = 0,

        ApproveConnection = 1, //requires mk

        ReadConnections = 2,

        UpdateConnections = 4,

        DeleteConnections = 8,

        CreateOrSendConnectionRequests = 16, //requires mk

        ReadConnectionRequests = 32,

        DeleteConnectionRequests = 64,

        ReadCircleMembership = 128,

        ManageCircleMembership = 256 //requires mk
    }
}