using System.Collections.Generic;
using Odin.Core.Identity;

namespace Odin.Services.ShamiraPasswordRecovery;

public class RemoteShardVerificationResult
{
    public Dictionary<string, bool> Players { get; init; } = new();
}