#nullable enable
using System;
using Odin.Core.Identity;
using Odin.Core.Services.Authorization.ExchangeGrants;
using Odin.Core.Util;

namespace Odin.Core.Services.Authentication.YouAuth;

public sealed class YouAuthUnifiedClient
{
    public YouAuthUnifiedClient(Guid id, SimpleDomainName domain, AccessRegistration accessReg)
    {
        this.Id = id;
        this.DomainName = domain;
        this.AccessRegistration = accessReg;
    }

    public YouAuthUnifiedClient()
    {
        //for Json deserialization
    }

    public Guid Id { get; init; }

    public SimpleDomainName DomainName { get; init; }

    public AccessRegistration? AccessRegistration { get; init; }
}