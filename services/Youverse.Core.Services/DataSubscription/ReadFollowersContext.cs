using System;
using Youverse.Core.Services.Authorization.Acl;
using Youverse.Core.Services.Base;

namespace Youverse.Core.Services.DataSubscription;

public class ReadFollowersContext : IDisposable
{
    private readonly SecurityGroupType _prevSecurityGroupType;
    private readonly DotYouContextAccessor _dotYouContextAccessor;

    public ReadFollowersContext(DotYouContextAccessor dotYouContextAccessor)
    {
        _dotYouContextAccessor = dotYouContextAccessor;
        _prevSecurityGroupType = _dotYouContextAccessor.GetCurrent().Caller.SecurityLevel;

        _dotYouContextAccessor.GetCurrent().Caller.SecurityLevel = SecurityGroupType.System;
    }

    public void Dispose()
    {
        _dotYouContextAccessor.GetCurrent().Caller.SecurityLevel = _prevSecurityGroupType;
    }
}