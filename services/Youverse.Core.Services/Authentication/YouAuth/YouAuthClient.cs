#nullable enable
using System;
using Youverse.Core.Identity;
using Youverse.Core.Services.Authorization.ExchangeGrants;

namespace Youverse.Core.Services.Authentication.YouAuth;

public sealed class YouAuthClient
{
    public YouAuthClient(Guid id, OdinId dotYouId, AccessRegistration accessReg)
    {
        this.Id = id;
        this.DotYouId = dotYouId;
        this.AccessRegistration = accessReg;
    }

    public YouAuthClient()
    {
        //for Json deserialization
    }

    public Guid Id { get; init; }

    public OdinId DotYouId { get; init; }

    public AccessRegistration AccessRegistration { get; init; }
}