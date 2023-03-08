#nullable enable
using System;
using Youverse.Core.Identity;
using Youverse.Core.Services.Authorization.ExchangeGrants;

namespace Youverse.Core.Services.Authentication.YouAuth;

public sealed class YouAuthClient
{
    public YouAuthClient(Guid id, OdinId odinId, AccessRegistration accessReg)
    {
        this.Id = id;
        this.OdinId = odinId;
        this.AccessRegistration = accessReg;
    }

    public YouAuthClient()
    {
        //for Json deserialization
    }

    public Guid Id { get; init; }

    public OdinId OdinId { get; init; }

    public AccessRegistration? AccessRegistration { get; init; }
}