using Odin.Core;
using Odin.Services.Authorization.ExchangeGrants;

namespace Odin.Services.Authorization.Apps;


public sealed class AppCredential
{
    public AppCredential(GuidId appId, string friendlyName, AccessRegistration accessRegistration)
    {
        GuidId.AssertIsValid(appId);

        AppId = appId;
        FriendlyName = friendlyName;
        AccessRegistration = accessRegistration;
    }

    public GuidId AppId { get; init; }

    public AccessRegistration AccessRegistration { get; init; }
    
    public string FriendlyName { get; init; }
}