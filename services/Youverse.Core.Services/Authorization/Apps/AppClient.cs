using Dawn;
using Youverse.Core.Services.Authorization.ExchangeGrants;

namespace Youverse.Core.Services.Authorization.Apps;

public sealed class AppClient
{
    public AppClient(GuidId appId, string friendlyName, AccessRegistration accessRegistration)
    {
        GuidId.AssertIsValid(appId);
        Guard.Argument(accessRegistration, nameof(accessRegistration)).NotNull();

        AppId = appId;
        FriendlyName = friendlyName;
        AccessRegistration = accessRegistration;
    }

    public GuidId AppId { get; init; }

    public AccessRegistration AccessRegistration { get; init; }
    public string FriendlyName { get; init; }
}