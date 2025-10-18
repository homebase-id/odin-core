#nullable enable
using System;
using Odin.Core;
using Odin.Core.Identity;
using Odin.Core.Serialization;
using Odin.Services.Authorization.ExchangeGrants;

namespace Odin.Hosting.Controllers.Home.Service;

public sealed class HomeAppClientRegistration(OdinId odinId, AccessRegistration accessReg, HomeAppClientType clientType)
    : IClientRegistration
{
    public OdinId OdinId { get; init; } = odinId;

    public AccessRegistration? AccessRegistration { get; init; } = accessReg;

    public HomeAppClientType ClientType { get; init; } = clientType;

    public Guid Id => AccessRegistration!.Id;
    public string IssuedTo => this.OdinId;
    public int Type => 200;
    public long TimeToLiveSeconds => 60 * 60 * 3; // 3 hours
    public Guid CategoryId => Guid.Empty;

    public string GetValue()
    {
        return OdinSystemSerializer.Serialize(this);
    }
}