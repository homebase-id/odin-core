#nullable enable
using System;
using Odin.Core;
using Odin.Core.Identity;
using Odin.Core.Serialization;
using Odin.Services.Authorization.ExchangeGrants;

namespace Odin.Hosting.Controllers.Home.Service;

public sealed class HomeAppClientRegistration : IClientRegistration
{
    public HomeAppClientRegistration()
    {
        //for json
    }

    public HomeAppClientRegistration(OdinId odinId, AccessRegistration accessReg, HomeAppClientType clientType)
    {
        OdinId = odinId;
        AccessRegistration = accessReg;
        ClientType = clientType;
    }

    public OdinId OdinId { get; init; }

    public AccessRegistration? AccessRegistration { get; init; }

    public HomeAppClientType ClientType { get; init; }

    public Guid Id => AccessRegistration!.Id;
    public string IssuedTo => this.OdinId;
    public int Type => 200;
    public long TimeToLiveSeconds => (long)TimeSpan.FromDays(60).TotalSeconds;
    public Guid CategoryId => Guid.Empty;

    public string GetValue()
    {
        return OdinSystemSerializer.Serialize(this);
    }
}