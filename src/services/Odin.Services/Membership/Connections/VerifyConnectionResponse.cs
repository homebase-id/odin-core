using System;

namespace Odin.Services.Membership.Connections;

public class VerifyConnectionResponse
{
    public bool IsConnected { get; init; }

    public byte[] Hash { get; init; }
}