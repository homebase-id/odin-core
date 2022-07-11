#nullable enable
using System;
using Youverse.Core.Identity;
using Youverse.Core.Services.Authorization.ExchangeGrants;

namespace Youverse.Core.Services.Authentication.YouAuth;

public sealed class YouAuthClient
{
    public YouAuthClient()
    {
        //for Litedb
    }
    public YouAuthClient(Guid id, DotYouIdentity dotYouId, AccessRegistration accessReg)
    {
        this.Id = id;
        this.DotYouId = dotYouId;
        this.AccessRegistration = accessReg;
    }

    public Guid Id { get; init; }

    public DotYouIdentity DotYouId { get; init; }

    public AccessRegistration AccessRegistration { get; init; }
}