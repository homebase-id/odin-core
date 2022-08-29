using Dawn;
using Youverse.Core.Services.Authorization.ExchangeGrants;

namespace Youverse.Core.Services.Authorization.Apps;

public sealed class AppClient
{
    public AppClient(ByteArrayId appId, AccessRegistration accessRegistration)
    {
        ByteArrayId.AssertIsValid(appId);
        Guard.Argument(accessRegistration, nameof(accessRegistration)).NotNull();
            
        AppId = appId;
        AccessRegistration = accessRegistration;
    }
        
    public ByteArrayId AppId { get; init; }

    public AccessRegistration AccessRegistration { get; init; }

}