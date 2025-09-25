using System.Collections.Generic;
using Odin.Core.Time;

namespace Odin.Services.Security.PasswordRecovery.Shamir;

/// <summary>
/// Unencrypted information used during the recovery process
/// </summary>
public class ShamirRecoveryInfo
{
    public UnixTimeUtc Created { get; set; }
    public List<ShamiraPlayer> Players { get; init; }
}