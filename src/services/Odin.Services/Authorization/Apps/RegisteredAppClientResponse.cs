using System;
using Odin.Core;
using Odin.Services.Authorization.ExchangeGrants;

namespace Odin.Services.Authorization.Apps;

public class RegisteredAppClientResponse
{
    public GuidId AppId { get; set; }

    public GuidId AccessRegistrationId { get; set; }

    public string FriendlyName { get; set; }

    public AccessRegistrationClientType AccessRegistrationClientType { get; set; }

    public Int64 Created { get; set; }

    public bool IsRevoked { get; set; }
}