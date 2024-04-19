using System;
using System.Collections.Generic;
using Odin.Services.Authorization.Acl;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;

namespace Odin.Services.DataSubscription.ReceivingHost;

public class FeedDriveDistributionSecurityContext : IDisposable
{
    private readonly IOdinContext _odinContext;
    private readonly SecurityGroupType _prevSecurityGroupType;

    private const string GroupName = "read_followers_only_for_distribution";

    public FeedDriveDistributionSecurityContext(ref IOdinContext odinContext)
    {
        _odinContext = odinContext;

        _prevSecurityGroupType = odinContext.Caller.SecurityLevel;

        //
        // Upgrade access briefly to perform functions
        //
        _odinContext.Caller.SecurityLevel = SecurityGroupType.Owner;

        //Note TryAdd because this might have already been added when multiple files are coming in

        _odinContext.PermissionsContext.PermissionGroups.TryAdd(GroupName,
            new PermissionGroup(
                new PermissionSet(new[] { PermissionKeys.ReadMyFollowers }),
                new List<DriveGrant>() { }, null, null));
    }

    public void Dispose()
    {
        _odinContext.Caller.SecurityLevel = _prevSecurityGroupType;
        _odinContext.PermissionsContext.PermissionGroups.Remove(GroupName);
    }
}