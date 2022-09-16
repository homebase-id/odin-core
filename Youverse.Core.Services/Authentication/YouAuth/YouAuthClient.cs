#nullable enable
using System;
using Youverse.Core.Identity;
using Youverse.Core.Services.Authorization.ExchangeGrants;

namespace Youverse.Core.Services.Authentication.YouAuth;

public sealed class YouAuthClient
{
    public YouAuthClient(Guid id, DotYouIdentity dotYouId, AccessRegistration accessReg, YouAuthClientAccessRegistrationType type)
    {
        this.Id = id;
        this.DotYouId = dotYouId;
        this.AccessRegistration = accessReg;
        this.AccessRegistrationType = type;
    }

    public YouAuthClient()
    {
        //for Json deserialization
    }

    public Guid Id { get; init; }

    public DotYouIdentity DotYouId { get; init; }

    public AccessRegistration AccessRegistration { get; init; }

    /// <summary>
    /// Specifies where we shoudl look up the access Registration
    /// </summary>
    public YouAuthClientAccessRegistrationType AccessRegistrationType { get; init; }
}

public enum YouAuthClientAccessRegistrationType
{
    YouAuth,
    IdentityConnectionRegistration
}