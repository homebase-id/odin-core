using System;
using Youverse.Core.Identity;
using Youverse.Core.Services.Authorization.ExchangeGrants;

namespace Youverse.Core.Services.Contacts.Circle.Membership;

public class IdentityConnectionRegistrationClient
{
    public Guid Id { get; init; }

    public OdinId OdinId { get; init; }

    public AccessRegistration AccessRegistration { get; init; }
}