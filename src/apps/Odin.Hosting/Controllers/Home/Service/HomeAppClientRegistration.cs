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

    public Guid Id
    {
        get => AccessRegistration!.Id;
        set { }
    }

    public OdinId IssuedTo
    {
        get => this.OdinId;
        set { }
    }

    public int Type
    {
        get => 200;
        set { }
    }

    public long TimeToLiveSeconds
    {
        get => 60 * 60 * 3; // 3 hours
        set { }
    }

    public Guid CategoryId
    {
        get => Guid.Empty;
        set { }
    }

    public string GetValue()
    {
        return OdinSystemSerializer.Serialize(this);
    }
}