#nullable enable
using Odin.Core.Time;

namespace Odin.Core.Services.Membership.YouAuth;

public enum ConsentRequirementType
{
    /// <summary>
    /// Always require consent
    /// </summary>
    Always = 10,
    
    /// <summary>
    /// Require consent after a certain expiration
    /// </summary>
    Expiring = 32,

    /// <summary>
    /// Never require consent
    /// </summary>
    Never = 0
}

public class ConsentRequirements
{
    public ConsentRequirementType ConsentRequirementType {get; set; }
    
    public UnixTimeUtc ConsentExpirationDateTime { get; set; }
}