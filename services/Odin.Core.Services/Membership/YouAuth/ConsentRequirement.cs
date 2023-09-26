#nullable enable
namespace Odin.Core.Services.Membership.YouAuth;

public enum ConsentRequirement
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