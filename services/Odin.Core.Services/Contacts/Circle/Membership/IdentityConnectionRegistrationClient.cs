using System;
using Odin.Core.Identity;
using Odin.Core.Services.Authorization.ExchangeGrants;

namespace Odin.Core.Services.Contacts.Circle.Membership;

public class IdentityConnectionRegistrationClient
{
    public Guid Id { get; init; }

    public OdinId OdinId { get; init; }

    public AccessRegistration AccessRegistration { get; init; }
}