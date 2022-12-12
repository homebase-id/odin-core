using System;
using Youverse.Core.Services.Authorization.ExchangeGrants;

namespace Youverse.Core.Services.Authorization.Apps;

public class RegisteredAppClientResponse
{
    public AccessRegistrationClientType AccessRegistrationClientType { get; set; }
        
    public UInt64 Created { get; set; }
    
    public bool IsRevoked { get; set; }

}