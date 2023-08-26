using System;
using Odin.Core.Identity;
using Odin.Core.Services.Authorization.ExchangeGrants;

namespace Odin.Hosting.Controllers.Home.Service;

public class IdentityConnectionRegistrationClient
{
    public Guid Id { get; init; }

    public OdinId OdinId { get; init; }

    public AccessRegistration AccessRegistration { get; init; }
}