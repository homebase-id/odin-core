using System;
using Odin.Core.Services.Authorization.Acl;
using Odin.Core.Services.Base;

namespace Odin.Core.Services.DataSubscription;

public class ReadFollowersContext : IDisposable
{
    private readonly SecurityGroupType _prevSecurityGroupType;
    private readonly OdinContextAccessor _odinContextAccessor;

    public ReadFollowersContext(OdinContextAccessor odinContextAccessor)
    {
        _odinContextAccessor = odinContextAccessor;
        _prevSecurityGroupType = _odinContextAccessor.GetCurrent().Caller.SecurityLevel;

        _odinContextAccessor.GetCurrent().Caller.SecurityLevel = SecurityGroupType.System;
    }

    public void Dispose()
    {
        _odinContextAccessor.GetCurrent().Caller.SecurityLevel = _prevSecurityGroupType;
    }
}