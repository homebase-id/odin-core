using System;

namespace Odin.Services.Security.PasswordRecovery.Shamir;

public class VerifyShardRequest
{
    public Guid ShardId { get; init; }
}
