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
}