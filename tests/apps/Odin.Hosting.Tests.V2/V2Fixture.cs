#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Autofac;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Odin.Core.Logging.Statistics.Serilog;
using Odin.Hosting.Tests.V2.Api;
using Odin.Hosting.Tests.V2.Hosting;
using Odin.Services.Authentication.Owner;
using Odin.Services.Base;
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
    /// The identity <see cref="SetupCaller"/> / <see cref="SetupCallerWithOwner"/> act as when the
    /// test doesn't name one. Defaults to the first entry of <see cref="HostIdentities"/>.
    /// </summary>
    /// <remarks>
    /// Override this rather than reordering <see cref="HostIdentities"/>. Ordering carries no
    /// meaning of its own, and relying on it fails silently: the identities a fixture boots are
    /// structurally identical, so acting as the wrong one usually still passes while testing
    /// something other than what was intended.
    /// </remarks>
    protected virtual string PrimaryIdentity => HostIdentities[0];

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
    /// <summary>
    /// When false, the baseline is taken against tenants that have never run initial setup — for a
    /// fixture whose system under test *is* that setup.
    /// </summary>
    /// <remarks>
    /// A flag rather than an override, because the owner login must happen either way: it sets the
    /// password <see cref="OdinHost.TakeBaselineAsync"/> needs, and a fixture that overrode
    /// <see cref="WarmTenantBaselineAsync"/> and dropped it got an obscure snapshot failure rather
    /// than a message. Three fixtures carried byte-identical overrides to express exactly this before
    /// the flag existed. Override the method itself only to add extra seed state.
    /// </remarks>
    protected virtual bool InitializeIdentities => true;

    protected virtual async Task WarmTenantBaselineAsync()
    {
        foreach (var identity in HostIdentities)
        {
            // The login is not optional: it sets the password the baseline snapshot requires.
            var owner = await LoginAsOwner(identity);

            if (!InitializeIdentities)
            {
                continue;
            }

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
    /// <para>
    /// <b>Events bleed between fixtures under <c>ParallelScope.Fixtures</c>.</b> An earlier version of
    /// this note claimed otherwise, on the grounds that <c>LogEventMemoryStore</c> is registered
    /// <c>SingleInstance</c> per Autofac container so each <see cref="OdinHost"/> owns its own store.
    /// The registration is per-host, but the store a write lands in is not. Measured: running
    /// <c>Ported/Transit/AppTransitQueryTestsForPublicFiles</c> beside a fixture known to log the
    /// #1771 error reddens the *former* with the latter's text, ~1 run in 5, in tests that make no
    /// peer call at all — while each fixture alone is clean over 6 runs.
    /// </para>
    /// <para>
    /// Mechanism, inferred and not yet confirmed: <c>UseSerilog</c> in <c>Program.cs</c> does not pass
    /// <c>preserveStaticLogger</c>, so every host boot reassigns the process-wide
    /// <c>Serilog.Log.Logger</c> — and the last host to boot owns the sink that injected
    /// <c>ILogger&lt;T&gt;</c> writes reach. Tracked in #1775.
    /// </para>
    /// <para>
    /// What this does and does not cost. The invariant still catches real defects — it found #1770,
    /// #1771 and #1772 in tests whose own assertions all passed. What it cannot currently do is
    /// attribute an error to the fixture that caused it, which is why a toleration has to be added to
    /// every fixture that might run alongside a producer rather than only the producer itself, and
    /// why this looked like unfixable flakiness the first time it was tried.
    /// </para>
    /// <para>
    /// Override to opt out for a fixture that deliberately provokes errors, and say why.
    /// </para>
    /// </remarks>
    protected virtual bool AssertNoErrorLogEvents => true;

    /// <summary>
    /// Error-log substrings this fixture tolerates. Prefer this over turning
    /// <see cref="AssertNoErrorLogEvents"/> off: it keeps the invariant live for everything else the
    /// fixture does, and names exactly what is expected. Give each entry a comment saying why —
    /// "the behaviour under test" or the issue number, never "this was noisy".
    /// </summary>
    protected virtual IReadOnlyCollection<string> ToleratedErrorLogSubstrings => [];

    /// <summary>
    /// The outbox logs every delivery failure at Error before rescheduling it
    /// (<c>PeerOutboxProcessorBackgroundService.ProcessItem</c>). A fixture whose subject is a
    /// delivery that cannot succeed — an introduction to a recipient who has disabled them, a
    /// connection that fails verification — should list this in
    /// <see cref="ToleratedErrorLogSubstrings"/>.
    /// </summary>
    /// <remarks>
    /// Deliberately not tolerated globally. "A transfer failed" is real signal for the peer fixtures
    /// that expect delivery to succeed, and suppressing it everywhere would hide exactly the failures
    /// this invariant is meant to surface. Each fixture that provokes a failure opts in by name.
    /// </remarks>
    protected const string OutboxDeliveryFailureLogged =
        "An outbox worker did not handle the outbox processing exception";



    /// <summary>
    /// Hook for fixtures that expect specific errors: override to inspect the events and assert
    /// whatever is right for that fixture. Mirrors the V1 <c>SetAssertLogEventsAction</c> escape
    /// hatch. Only called when <see cref="AssertNoErrorLogEvents"/> is true. Most fixtures want
    /// <see cref="ToleratedErrorLogSubstrings"/> instead of overriding this.
    /// </summary>
    protected virtual void AssertLogEvents(Dictionary<LogEventLevel, List<LogEvent>> logEvents)
    {
        // Deliberately not LogEvents.AssertEvents: that asserts on a count and prints
        // "Expected: 0, But was: 1", which says a test logged an error but not which one or why.
        // The whole value of this invariant is that it fires on paths the test never looks at, so the
        // message has to carry the event. Two of the three product bugs it has found so far were
        // diagnosed straight out of this text.
        var errors = ErrorsIn(logEvents, LogEventLevel.Error)
            .Concat(ErrorsIn(logEvents, LogEventLevel.Fatal))
            .Where(NotTolerated)
            .ToList();

        if (errors.Count == 0)
        {
            return;
        }

        var rendered = string.Join("\n\n", errors.Select((evt, i) =>
            $"  [{i + 1}/{errors.Count}] {evt.Level}: {evt.RenderMessage()}" +
            (evt.Exception == null ? "" : $"\n      {evt.Exception.GetType().Name}: {evt.Exception.Message}")));

        Assert.Fail(
            $"The server logged {errors.Count} error-level event(s) during this test.\n{rendered}\n\n" +
            "If an error here is the behaviour under test, add its text to ToleratedErrorLogSubstrings " +
            "with a reason. Turn AssertNoErrorLogEvents off only if the whole fixture is about error paths.");

        static IEnumerable<LogEvent> ErrorsIn(Dictionary<LogEventLevel, List<LogEvent>> events, LogEventLevel level)
            => events.TryGetValue(level, out var found) ? found : [];

        bool NotTolerated(LogEvent evt)
        {
            if (ToleratedErrorLogSubstrings.Count == 0)
            {
                return true;
            }

            var text = evt.RenderMessage() + " " + (evt.Exception?.Message ?? "");
            return !ToleratedErrorLogSubstrings.Any(text.Contains);
        }
    }

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

    /// <summary>Logs in as <see cref="PrimaryIdentity"/> — the common case for single-identity fixtures.</summary>
    protected Task<OwnerSession> LoginAsOwner() => LoginAsOwner(PrimaryIdentity);

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
        var owner = await LoginAsOwner(ownerIdentity ?? PrimaryIdentity);
        var d = spec.DriveSpec;
        await owner.Admin.EnsureDrive(d.Drive, d.Name, d.AllowAnonymousReads, d.OwnerOnly, d.AllowSubscriptions,
            d.Attributes);
        var caller = await spec.Build(owner);
        return (caller, owner);
    }

    /// <summary>
    /// An owner <see cref="IOdinContext"/> and the tenant scope it came from — what a test needs to call
    /// a service (a version-upgrade pass, say) directly rather than over HTTP.
    /// </summary>
    /// <remarks>
    /// Here rather than in each fixture: a dozen-odd fixtures once grew their own byte-identical copy of
    /// this, so a change to how the context is built (a new <c>OdinClientContext</c> field, say) meant a
    /// dozen edits.
    /// </remarks>
    protected async Task<(ILifetimeScope Scope, IOdinContext Context)> MigrationContextAsync(OwnerSession owner)
    {
        var scope = Host.GetTenantScope(owner.Identity.DomainName);
        return (scope, await BuildOwnerContextAsync(scope, owner));
    }

    /// <summary>
    /// An owner context carrying the master key, built from a logged-in owner session.
    /// </summary>
    protected static async Task<IOdinContext> BuildOwnerContextAsync(ILifetimeScope scope, OwnerSession owner)
    {
        var authService = scope.Resolve<OwnerAuthenticationService>();
        var odinContext = new OdinContext();

        await authService.UpdateOdinContextAsync(owner.Token, new OdinClientContext(), odinContext);
        odinContext.Caller!.AssertHasMasterKey();
        return odinContext;
    }
}
