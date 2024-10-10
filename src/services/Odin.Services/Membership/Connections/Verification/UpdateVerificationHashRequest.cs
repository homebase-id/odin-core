using System;

namespace Odin.Services.Membership.Connections.Verification;

public class UpdateVerificationHashRequest
{
    public Guid RandomCode { get; init; }
}