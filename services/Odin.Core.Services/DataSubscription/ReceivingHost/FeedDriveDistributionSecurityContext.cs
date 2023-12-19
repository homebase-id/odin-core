using System;
using System.Collections.Generic;
using System.Threading;
using Odin.Core.Services.Authorization.Acl;
using Odin.Core.Services.Authorization.ExchangeGrants;
using Odin.Core.Services.Authorization.Permissions;
using Odin.Core.Services.Base;

namespace Odin.Core.Services.DataSubscription.ReceivingHost;

public class FeedDriveDistributionSecurityContext : IDisposable
{
    private readonly SecurityGroupType _prevSecurityGroupType;
    private readonly OdinContextAccessor _odinContextAccessor;

    private const string GroupName = "read_followers_only_for_distribution";

    // private static SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);

    public FeedDriveDistributionSecurityContext(OdinContextAccessor odinContextAccessor)
    {
        _odinContextAccessor = odinContextAccessor;
        var ctx = odinContextAccessor.GetCurrent();

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
        _odinContextAccessor.GetCurrent().Caller.SecurityLevel = _prevSecurityGroupType;
        _odinContextAccessor.GetCurrent().PermissionsContext.PermissionGroups.Remove(GroupName);
    }
}