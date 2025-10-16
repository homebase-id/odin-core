#nullable enable
using Odin.Core.Identity;
using Odin.Services.Authorization.ExchangeGrants;

namespace Odin.Hosting.Controllers.Home.Service;

public sealed class HomeAppClient
{
    public HomeAppClient(OdinId odinId, AccessRegistration accessReg, HomeAppClientType clientType)
    {
        this.OdinId = odinId;
        this.AccessRegistration = accessReg;
        this.ClientType = clientType;
    }

    public HomeAppClient()
    {
        //for Json deserialization
    }

    public OdinId OdinId { get; init; }
    
    public AccessRegistration? AccessRegistration { get; init; }
    
    public HomeAppClientType ClientType { get; init; }

}