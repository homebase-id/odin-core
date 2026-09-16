#nullable enable
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Odin.Core.Logging.Statistics.Serilog;
using Odin.Hosting.Tests.V2.Api;
using Odin.Hosting.Tests.V2.Hosting;
using Odin.Test.Helpers.Logging;
using Serilog.Events;

namespace Odin.Hosting.Tests.V2;

/// <summary>
/// Base fixture for fast V2 tests. Boots one in-process <see cref="OdinHost"/> per test class with
/// the identities declared by <see cref="HostIdentities"/>; tears it down once at the end. Every
/// <c>[Test]</c> starts against a freshly-restored identity DB and an empty payload tree
/// (see <see cref="OdinHost.ResetAsync"/>) — opt out with <see cref="ResetBetweenTests"/> for
/// pure-read fixtures. Fixtures run in parallel; <see cref="OdinHost"/> isolates per-host config
/// via in-memory providers, so different fixtures' data roots / preconfigured domains never collide.
/// </summary>
[Parallelizable(ParallelScope.Fixtures)]
public abstract class V2Fixture
{
    protected OdinHost Host { get; private set; } = null!;

    /// <summary>Identities this fixture needs preconfigured. Default is Frodo only.</summary>
    protected virtual string[] HostIdentities => [Identities.Frodo];

    /// <summary>
    /// When true (default), each <c>[Test]</c> starts against a freshly-restored copy of the
    /// identity DB and an empty payload tree — see <see cref="OdinHost.ResetAsync"/>. Opt out
    /// for fixtures that don't write state (e.g. smoke / ping tests) to skip the reset cost.
    /// </summary>
    protected virtual bool ResetBetweenTests => true;

    /// <summary>
    /// Configuration this fixture needs the host booted with, merged over the per-host defaults.
    /// Override for settings that must be in place before startup — e.g. a mail fixture turning on
    /// <c>Email:TenantMail:Enabled</c>. Prefer this over environment variables: fixtures run in
    /// parallel and env vars are process-wide, so one fixture's flag would leak into every other.
    /// List settings bind by index (<c>"Email:TenantMail:MxNodes:0"</c>), not the <c>__0</c> form.
    /// </summary>
    protected virtual IReadOnlyDictionary<string, string?> ConfigOverrides =>
        new Dictionary<string, string?>();

    [OneTimeSetUp]
    public async Task V2FixtureSetUp()
    {
        Host = await OdinHost.StartAsync(HostIdentities, ConfigOverrides);
        if (ResetBetweenTests)
        {
            await Host.EnsureTenantsMaterializedAsync();
            await WarmTenantBaselineAsync();
            await Host.TakeBaselineAsync();
        }
    }

    /// <summary>
    /// Bake the per-identity baseline state into the host before snapshotting: log in as owner of
    /// each preconfigured identity (sets the password, required for snapshot baseline) and run the
    /// tenant initial-setup flow which creates system circles + system drives. The peer
    /// connection-request flow grants the <c>ConfirmedConnections</c> system circle on connect;
    /// without it, <c>CircleMembershipService.CreateCircleGrantListAsync</c> throws "Missing circle
    /// Id". Idempotent; override to add fixture-specific seed state before the snapshot is taken.
    /// </summary>
    protected virtual async Task WarmTenantBaselineAsync()
    {
        foreach (var identity in HostIdentities)
        {
            var owner = await LoginAsOwner(identity);
            var resp = await owner.Admin.InitializeIdentity();
            if (!resp.IsSuccessStatusCode)
            {
                throw new System.InvalidOperationException(
                    $"InitializeIdentity failed for {identity}: {resp.StatusCode}");
            }
        }
    }

    /// <summary>
    /// When true (default), a test fails if the server logged an Error or Fatal during it, even when
    /// every explicit assertion passed — the invariant the V1 <c>WebScaffold</c> fixtures enforced via
    /// <c>AssertLogEvents</c>. It is what catches swallowed exceptions and error-level noise on paths
    /// the test itself never looks at.
    /// </summary>
    /// <remarks>
    /// Safe under <c>ParallelScope.Fixtures</c>: <c>LogEventMemoryStore</c> is registered
    /// <c>SingleInstance</c> per Autofac container (<c>LoggingAutofacModule</c>), so each
    /// <see cref="OdinHost"/> owns its own store, and the sink is bound to that host's store at
    /// startup. The one process-wide vector is static <c>Serilog.Log.*</c>, whose only Error/Fatal
    /// call sites are host-termination and Let's Encrypt issuance — neither reachable from this
    /// TLS-less host.
    ///
    /// Override to opt out for a fixture that deliberately provokes errors, and say why.
    /// </remarks>
    protected virtual bool AssertNoErrorLogEvents => true;

    /// <summary>
    /// Hook for fixtures that expect specific errors: override to inspect the events and assert
    /// whatever is right for that fixture. Mirrors the V1 <c>SetAssertLogEventsAction</c> escape
    /// hatch. Only called when <see cref="AssertNoErrorLogEvents"/> is true.
    /// </summary>
    protected virtual void AssertLogEvents(Dictionary<LogEventLevel, List<LogEvent>> logEvents) =>
        LogEvents.AssertEvents(logEvents);

    [SetUp]
    public async Task V2FixturePerTestSetUp()
    {
        if (ResetBetweenTests && Host != null)
        {
            await Host.ResetAsync();
        }

        // Clear AFTER the reset — restoring the DB and wiping the payload tree can itself log.
        // One host serves the whole fixture, so without this an error in test #1 fails test #2.
        LogStore?.Clear();
    }

    [TearDown]
    public void V2FixturePerTestTearDown()
    {
        if (!AssertNoErrorLogEvents || LogStore == null)
        {
            return;
        }

        AssertLogEvents(LogStore.GetLogEvents());
    }

    private ILogEventMemoryStore? LogStore =>
        Host?.Server.Services.GetService<ILogEventMemoryStore>();

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
    /// <paramref name="ownerIdentity"/> (default: the fixture's first <see cref="HostIdentities"/>),
    /// creates the spec's <see cref="CallerSpec.TargetDrive"/>, then builds and returns the caller
    /// (Owner / App / Guest).
    /// </summary>
    protected async Task<IV2Caller> SetupCaller(CallerSpec spec, string? ownerIdentity = null)
    {
        var (caller, _) = await SetupCallerWithOwner(spec, ownerIdentity);
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
        string? ownerIdentity = null)
    {
        var owner = await LoginAsOwner(ownerIdentity ?? HostIdentities[0]);
        var d = spec.DriveSpec;
        await owner.Admin.EnsureDrive(d.Drive, d.Name, d.AllowAnonymousReads, d.OwnerOnly, d.AllowSubscriptions);
        var caller = await spec.Build(owner);
        return (caller, owner);
    }
}
