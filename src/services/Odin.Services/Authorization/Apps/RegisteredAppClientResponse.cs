using System;
using Odin.Core;
using Odin.Core.Time;
using Odin.Services.Authorization.ExchangeGrants;

namespace Odin.Services.Authorization.Apps;

public class RegisteredAppClientResponse
{
    public GuidId AppId { get; set; }

    public GuidId AccessRegistrationId { get; set; }

    public string FriendlyName { get; set; }

    public AccessRegistrationClientType AccessRegistrationClientType { get; set; }

    public UnixTimeUtc Created { get; set; }

    public bool IsRevoked { get; set; }

    /// <summary>
    /// Whether the device was handed the app's current zero-knowledge key: one that wasn't sees the app's
    /// zero-knowledge drives as empty until it signs in again (docs/zero-knowledge-drive-api-plan.md, 3.4 and 4.3).
    /// </summary>
    public bool HoldsZkAppKey { get; set; }
}