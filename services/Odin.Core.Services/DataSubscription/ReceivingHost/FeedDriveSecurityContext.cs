using System;
using Odin.Core.Services.Authorization.Acl;
using Odin.Core.Services.Base;

namespace Odin.Core.Services.DataSubscription.ReceivingHost;

public class FeedDriveSecurityContext : IDisposable
{
    private readonly SecurityGroupType _prevSecurityGroupType;
    private readonly OdinContextAccessor _odinContextAccessor;

    public FeedDriveSecurityContext(OdinContextAccessor odinContextAccessor)
    {
        _odinContextAccessor = odinContextAccessor;
        _prevSecurityGroupType = _odinContextAccessor.GetCurrent().Caller.SecurityLevel;

        _odinContextAccessor.GetCurrent().Caller.SecurityLevel = SecurityGroupType.Owner;
        // _dotYouContextAccessor.GetCurrent().SetPermissionContext();
    }

    public void Dispose()
    {
        _odinContextAccessor.GetCurrent().Caller.SecurityLevel = _prevSecurityGroupType;
    }
}