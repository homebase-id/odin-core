using System;

namespace Odin.Services.Membership.Connections.Requests;

public class VerificationCode
{
    public Guid Code { get; init; }
}