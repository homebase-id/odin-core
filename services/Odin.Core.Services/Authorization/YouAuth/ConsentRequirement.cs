#nullable enable
namespace Odin.Core.Services.Authorization.YouAuth;

public enum ConsentRequirement
{
    /// <summary>
    /// Always require consent
    /// </summary>
    Always,


    // /// <summary>
    // /// Require consent after a certain expiration
    // /// </summary>
    // Expiring,

    /// <summary>
    /// Never require consent
    /// </summary>
    Never
}