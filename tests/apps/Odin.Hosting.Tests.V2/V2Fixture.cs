#nullable enable
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Hosting.Tests.V2.Api;
using Odin.Hosting.Tests.V2.Hosting;

namespace Odin.Hosting.Tests.V2;

/// <summary>
/// Base fixture for fast V2 tests. Boots one in-process <see cref="OdinHost"/> per test class with
/// the identities declared by <see cref="HostIdentities"/>; tears it down once at the end.
/// Tests within a class share the host (and any seeded state) — per-test DB reset is a later phase.
/// Fixtures run in parallel: <see cref="OdinHost"/> isolates per-host config via in-memory
/// providers, so different fixtures' data roots / preconfigured domains never collide.
/// </summary>
[Parallelizable(ParallelScope.Fixtures)]
public abstract class V2Fixture
{
    protected OdinHost Host { get; private set; } = null!;

    /// <summary>Identities this fixture needs preconfigured. Default is Frodo only.</summary>
    protected virtual string[] HostIdentities => [Identities.Frodo];

    [OneTimeSetUp]
    public async Task V2FixtureSetUp()
    {
        Host = await OdinHost.StartAsync(HostIdentities);
    }

    [OneTimeTearDown]
    public async Task V2FixtureTearDown()
    {
        if (Host != null)
        {
            await Host.DisposeAsync();
        }
    }

    /// <summary>
    /// Performs the owner login dance and returns a session bundling the issued token, shared secret,
    /// a configured API factory, and ready-to-use V2 client wrappers.
    /// </summary>
    protected Task<OwnerSession> LoginAsOwner(string identity) => OwnerSession.LoginAsync(Host, identity);

    /// <summary>
    /// One-liner for parameterized tests over <see cref="CallerSpec"/>: logs in as owner of
    /// <paramref name="ownerIdentity"/> (default Frodo), creates the spec's <see cref="CallerSpec.TargetDrive"/>,
    /// then builds and returns the caller (Owner / App / Guest).
    /// </summary>
    protected async Task<IV2Caller> SetupCaller(CallerSpec spec, string? ownerIdentity = null, bool allowAnonymousReads = true)
    {
        var (caller, _) = await SetupCallerWithOwner(spec, ownerIdentity, allowAnonymousReads);
        return caller;
    }

    /// <summary>
    /// Variant of <see cref="SetupCaller"/> that also returns the owner session — useful when a test
    /// needs to validate post-call state via the owner's reader (e.g. the App/Guest caller wrote a
    /// file and we want to confirm it as owner) or when the test wants the owner to seed the drive
    /// with content the caller then reads.
    /// </summary>
    protected async Task<(IV2Caller Caller, OwnerSession Owner)> SetupCallerWithOwner(
        CallerSpec spec,
        string? ownerIdentity = null,
        bool allowAnonymousReads = true)
    {
        var owner = await LoginAsOwner(ownerIdentity ?? Identities.Frodo);
        await owner.Admin.CreateDrive(spec.TargetDrive, "Test Drive", allowAnonymousReads);
        var caller = await spec.Build(owner);
        return (caller, owner);
    }
}
