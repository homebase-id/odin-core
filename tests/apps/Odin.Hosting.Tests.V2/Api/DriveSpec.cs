using Odin.Services.Drives;

namespace Odin.Hosting.Tests.V2.Api;

/// <summary>
/// Bundles the <see cref="TargetDrive"/> together with the creation options the fixture should use
/// when calling <c>CreateDrive</c>. Carried by <see cref="CallerSpec"/> so adding a new drive option
/// (owner-only, subscriptions, custom name) lands here instead of as another positional flag on
/// <c>V2Fixture.SetupCaller</c>.
/// </summary>
public sealed record DriveSpec(
    TargetDrive Drive,
    string Name = "Test Drive",
    bool AllowAnonymousReads = true,
    bool OwnerOnly = false,
    bool AllowSubscriptions = false)
{
    /// <summary>Fresh anonymous-readable drive — the default for V2 tests.</summary>
    public static DriveSpec Anon() => new(TargetDrive.NewTargetDrive());

    /// <summary>Fresh drive with anonymous reads disabled — for ACL-gated read tests.</summary>
    public static DriveSpec Secured() => new(TargetDrive.NewTargetDrive(), AllowAnonymousReads: false);
}
