using System.Collections.Generic;
using Odin.Core;
using Odin.Services.Membership.Circles;
using Odin.Services.Membership.Connections.Requests;
using Odin.Services.Util;

namespace Odin.Services.Membership.Connections;

public static class CircleNetworkUtils
{
    public static List<GuidId> EnsureSystemCircles(List<GuidId> circleIds, ConnectionRequestOrigin origin)
    {
        var list = circleIds ?? new List<GuidId>();

        // Always put identities in the system circle
        list.EnsureItem(SystemCircleConstants.ConnectedIdentitiesSystemCircleId);

        switch (origin)
        {
            case ConnectionRequestOrigin.IdentityOwner:
                list.EnsureItem(SystemCircleConstants.ConfirmedConnectionsCircleId);
                break;
            case ConnectionRequestOrigin.Introduction:
                list.EnsureItem(SystemCircleConstants.AutoConnectionsCircleId);
                break;
        }

        return list;
    }
}