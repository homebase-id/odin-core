using System;
using System.Collections.Generic;
using Odin.Core.Services.Authorization.Acl;
using Odin.Core.Services.Authorization.ExchangeGrants;
using Odin.Core.Services.Authorization.Permissions;
using Odin.Core.Services.Base;

namespace Odin.Core.Services.DataSubscription.ReceivingHost;

public class FeedDriveHistoryDistributorSecurityContext : IDisposable
{
    private readonly SecurityGroupType _prevSecurityGroupType;
    private readonly OdinContextAccessor _odinContextAccessor;

    private const string GroupName = "read_followers_only";
    public FeedDriveHistoryDistributorSecurityContext(OdinContextAccessor odinContextAccessor)
    {
        _odinContextAccessor = odinContextAccessor;
        var ctx = odinContextAccessor.GetCurrent();
        
        _prevSecurityGroupType = ctx.Caller.SecurityLevel;

        //
        // Upgrade access briefly to perform functions
        //
        ctx.Caller.SecurityLevel = SecurityGroupType.Owner;
        ctx.PermissionsContext.PermissionGroups.Add(GroupName,
            new PermissionGroup(
                new PermissionSet(new[] { PermissionKeys.UseTransitWrite }), //to allow sending files
                new List<DriveGrant>() { }, null, null));
        
    }

    public void Dispose()
    {
        _odinContextAccessor.GetCurrent().Caller.SecurityLevel = _prevSecurityGroupType;
        _odinContextAccessor.GetCurrent().PermissionsContext.PermissionGroups.Remove(GroupName);
    }
}