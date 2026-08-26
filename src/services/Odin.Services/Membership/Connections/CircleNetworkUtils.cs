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

        switch (origin)
        {
            case ConnectionRequestOrigin.IdentityOwner:
                list.EnsureItem(SystemCircleConstants.ConfirmedConnectionsCircleId);

                // Same condition as CircleNetworkService.StampsReviewedOnConnect: an owner-driven request
                // is the review happening at accept time, so the contact is reviewed and belongs in the
                // reviewed circle from the same moment. Granted here rather than after the connection is
                // finalized because here the key store key is in hand -- finalization runs on the sender's
                // side as an incoming peer callback, which holds neither the master key nor the drive
                // permissions a deposit would need.
                list.EnsureItem(SystemCircleConstants.ReviewedConnectionsCircleId);
                break;
            case ConnectionRequestOrigin.Introduction:
            case ConnectionRequestOrigin.IdentityOwnerApp:
                list.EnsureItem(SystemCircleConstants.AutoConnectionsCircleId);
                break;
        }

        return list;
    }
}