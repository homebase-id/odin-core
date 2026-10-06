namespace Odin.Services.Configuration.VersionUpgrade;

/// <summary>
/// What an identity's data upgrade is doing, as one answer.
/// </summary>
/// <remarks>
/// This used to be two booleans that a caller had to combine: <c>requiresUpgrade</c>, a comparison
/// of the tenant's data version against the release's, and <c>IsRunning</c>, the flag
/// <c>VersionUpgradeMiddleware</c> refuses requests on. They answer different questions and
/// disagree in both directions, which is a trap rather than a nuance:
/// <list type="bullet">
/// <item>An upgrade is scheduled when its owner authenticates but does not start until a background
/// job picks it up. In that window the version is behind and nothing is running -- <see cref="Pending"/>,
/// a state neither boolean names, and the one a YouAuth sign-in lands in.</item>
/// <item>The version is written before the run ends, so a caller watching the version alone thinks
/// it is finished while the server is still refusing everything.</item>
/// <item>A run that ends without reaching the release version leaves the version behind and nothing
/// running -- indistinguishable from <see cref="Pending"/> unless failure info is consulted too.</item>
/// </list>
/// </remarks>
public enum UpgradeState
{
    /// <summary>The tenant's data is at the release's version. Nothing to do.</summary>
    UpToDate = 0,

    /// <summary>An upgrade is needed and has not started. Requests are not being refused yet.</summary>
    Pending = 1,

    /// <summary>An upgrade is running. Nearly every request is refused until it ends.</summary>
    Running = 2,

    /// <summary>An upgrade is needed and the last attempt did not get there.</summary>
    Failed = 3
}
