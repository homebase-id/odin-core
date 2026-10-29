using System.Collections.Generic;

namespace Odin.Services.Security.PasswordRecovery.Shamir;

/// <summary>
/// The automated deal as the browser deals it: this host's automated identities, all automatic, and
/// the threshold (docs/zero-knowledge-recovery-plan.md, "Building the automated deal").
/// </summary>
public sealed class AutomatedRecoveryPlayers
{
    public List<ShamiraPlayer> Players { get; init; }
    public int MinMatchingShards { get; init; }
}
