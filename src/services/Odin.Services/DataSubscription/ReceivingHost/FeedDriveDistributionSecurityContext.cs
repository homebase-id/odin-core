using System;
using System.Collections.Generic;
using System.Threading;
using Odin.Services.Authorization.Acl;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;

namespace Odin.Services.DataSubscription.ReceivingHost;

public class FeedDriveDistributionSecurityContext : IDisposable
{
    private readonly SecurityGroupType _prevSecurityGroupType;

    private const string GroupName = "read_followers_only_for_distribution";

    // private static SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);

    public FeedDriveDistributionSecurityContext()
    {
        var ctx = odinContext;

        _prevSecurityGroupType = ctx.Caller.SecurityLevel;

        //
        // Upgrade access briefly to perform functions
        //
        ctx.Caller.SecurityLevel = SecurityGroupType.Owner;

        //Note TryAdd because this might have already been added when multiple files are coming in

        ctx.PermissionsContext.PermissionGroups.TryAdd(GroupName,
            new PermissionGroup(
                new PermissionSet(new[] { PermissionKeys.ReadMyFollowers }),
                new List<DriveGrant>() { }, null, null));
    }

    public void Dispose()
    {
        _odinodinContext.Caller.SecurityLevel = _prevSecurityGroupType;
        _odinodinContext.PermissionsContext.PermissionGroups.Remove(GroupName);
    }
}