using System;

namespace Odin.Services.Membership.Connections;

public class VerifyConnectionResponse
{
    public Guid VerificationCode {get; init; }
}