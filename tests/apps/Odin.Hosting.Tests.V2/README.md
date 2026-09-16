# Odin.Hosting.Tests.V2 — fast V2 integration test framework

In-process integration tests for Odin's V2 REST API. No Kestrel, no TLS, no ports —
`Microsoft.AspNetCore.TestHost.TestServer` boots the same `Program.CreateHostBuilder` Odin
serves with in production, and tests talk to it over an `HttpMessageHandler` rather than
the wire.

| | This framework | Old `WebScaffold` |
|---|---|---|
| Per-fixture boot | ~1.6 s cold, ~500 ms warm | 2–5 s |
| Per-test cost | ~5–20 ms (snapshot restore + payload wipe) | none (state leaks) |
| Fixture parallelism | yes (`ParallelScope.Fixtures`) | no (fixed ports) |
| Peer-to-peer flows | in-process, ~1 s end-to-end | over real loopback HTTPS |
| Wall clock (full V2 suite) | ~25 s (229 tests) | ~55 s (249 tests on Kestrel + TLS) |

Coexists with the V1 framework. V1 controller tests are being migrated here — see **Porting rules** below.

---

## Your first test

```csharp
[TestFixture]
public class MyDriveTests : V2Fixture
{
    public static IEnumerable<object[]> Cases()
    {
        yield return [CallerSpec.Owner(DriveSpec.Anon()),                       HttpStatusCode.OK];
        yield return [CallerSpec.App(DriveSpec.Anon(),   DrivePermission.Write), HttpStatusCode.OK];
        yield return [CallerSpec.Guest(DriveSpec.Anon(), DrivePermission.Read),  HttpStatusCode.Forbidden];
    }

    [Test, TestCaseSource(nameof(Cases))]
    public async Task CanUploadMetadata(CallerSpec spec, HttpStatusCode expected)
    {
        var caller = await SetupCaller(spec);
        var resp = await caller.Drives.Writer.UploadNewMetadata(
            spec.TargetDrive.Alias, SampleMetadataData.Create(fileType: 100));
        Assert.That(resp.StatusCode, Is.EqualTo(expected));
    }
}
```

That's it. The fixture handles host boot, tenant materialization, owner-login warm-up,
DB snapshot, and per-test restore. The `[TestCaseSource]` block fans out across Owner /
App / Guest with the access-control matrix.

---

## Mental model

```
V2Fixture (per test class)
  │
  └── OdinHost  (TestServer + multi-tenant container)
        │
        ├── Tenants: Frodo, Sam, …   ← preconfigured via HostIdentities
        ├── Snapshot: identity.db.snap per tenant
        └── ResetBetweenTests: file-copy + payload wipe before every [Test]

Each test method asks the fixture for a caller:
  IV2Caller  ←  Owner | App | Guest
    .Drives.Reader   (DriveReaderV2Client)
    .Drives.Writer   (DriveWriterV2Client)
    .Drives.Reactions (DriveGroupReactionV2Client)
    .Auth            (AuthV2Client)
    .Sync            (ITestSync — only on OwnerSession; drain hooks)
    .Admin           (only on OwnerSession; V1 admin endpoints for setup)
```

- **`V2Fixture`** is the base class. Override `HostIdentities` to add Sam, Pippin, etc.
  Override `ResetBetweenTests = false` for read-only smoke fixtures.
- **`SetupCaller(CallerSpec)`** does login + drive create + caller build. One line.
  Use `SetupCallerWithOwner` if you also need the owner for cross-actor assertions.
- **`OwnerSession.Sync.DrainOutboxAsync()` / `ProcessInboxAsync(drive)`** replace the
  V1 `WaitForEmptyOutbox` / `ProcessInbox` HTTP polling — instant local calls.

---

## Peer-to-peer flows

Frodo → Sam runs in the same process; no real network.

```csharp
[TestFixture]
public class FrodoToSamTransfer : V2Fixture
{
    protected override string[] HostIdentities => [Identities.Frodo, Identities.Sam];

    [Test]
    public async Task Send()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam   = await LoginAsOwner(Identities.Sam);

        var drive = TargetDrive.NewTargetDrive();
        await frodo.Admin.CreateDrive(drive, "frodo's");
        await sam.Admin.CreateDrive(drive,   "sam's");
        await PeerFlow.ConnectAsync(frodo, sam, drive, DrivePermission.Write);

        var metadata = SampleMetadataData.Create(fileType: 100, acl: AccessControlList.Connected);
        metadata.AllowDistribution = true;
        var send = await frodo.Drives.Writer.UploadNewMetadata(drive.Alias, metadata,
            transitOptions: new TransitOptions { Recipients = [sam.Identity] });

        await frodo.Sync.DrainOutboxAsync();
        await sam.Sync.ProcessInboxAsync(drive);

        // Assert via GlobalTransitId — Sam's FileId differs from Frodo's.
    }
}
```

Three test-only seams make this work:
- `TestPeerHttpClientFactory` routes outbound peer HTTP back to `TestServer.CreateHandler()`.
- `PeerCapiAuthenticationHandler` consults an optional `ITestPeerIdentityProvider` (production never registers an impl); the test-side impl reads `X-Test-Peer-Identity` from the request.
- `NoopBackgroundServiceNotifier<T>` stops production code from waiting 30 s for the
  outbox processor to start (the processor is registered in DI but never `StartAsync`'d
  in tests; drain is driven via `ITestSync`).

---

## Non-goals

- **V1 controller tests are no longer a non-goal.** V1 endpoints work here: the V1-shaped Refit clients from `_Universal` are reused unchanged, and `InProcessApiClientFactory` resolves their relative paths against the caller's own V1 base (`/api/owner/v1`, `/api/apps/v1`, `/api/guest/v1`) so Owner/App/Guest permission matrices behave as they do on Kestrel. See **Porting rules**.
- **mTLS-bound paths** — V2 tests run TLS-less; anything that genuinely requires client cert auth has to stay on real Kestrel.
- **Background-service timer behavior** — services are registered but never started. Anything time-driven (cert renewal, orphan scan, scheduled jobs) needs the V1 framework. Tests drain the peer outbox explicitly via `Sync.DrainOutboxAsync` and process the inbox via `Sync.ProcessInboxAsync`.
- **WebSocket-driven flows** — the host registers `SharedDeviceSocketCollection` but no V2 test currently opens a socket. The reset path does *not* clear those registries; the first test that holds a socket across the boundary will need to add a drain hook (see the "What this does NOT reset" note on `OdinHost.ResetAsync`).

---

## Porting rules

Migrating a `WebScaffold` fixture here. These are decisions already made — follow them rather than
re-deriving, which is how the first batches ended up with three spellings of the same thing.

**Shape**
- Delete the whole `WebScaffold` block (`_scaffold`, `[OneTimeSetUp]`, `[OneTimeTearDown]`,
  `[SetUp]`, `[TearDown]`). `V2Fixture` owns the lifecycle.
- One `IEnumerable<object[]> <Thing>Cases()` per distinct matrix, rows inline, consumed by a
  one-line `[Test, TestCaseSource(nameof(XCases))]`. Not one source per caller with stacked
  attributes. Never call it `TestCases`.
- Parameter is `expected`, not `expectedStatusCode`.
- `_Universal` context types map to `CallerSpec`: `OwnerClientContext` → `CallerSpec.Owner`,
  `AppWriteOnlyAccessToDrive` → `CallerSpec.App(..., Write)`, `AppSpecifyDriveAccess` with a
  `TestPermissionKeyList` → the three-argument `CallerSpec.App`, guests likewise. Call
  `DriveSpec.Anon()` / `.Secured()` per row so each case gets a fresh drive.
- A fixture from `OwnerApi/` usually has no caller matrix at all. Plain `[Test]` methods are correct
  — don't invent one.

**Clients**
- V1 clients come from the handles: `caller.V1.Drive`, `owner.V1.Reactions`, … Never
  `new UniversalXApiClient(identity, factory)` — that spelling makes it easy to pair one caller's
  identity with another's factory.
- `owner.Admin` is **arrange-only**: opinionated defaults, throws on non-2xx. When an admin endpoint
  is itself the system under test — including every test that asserts a refusal — call the Refit
  interface through `owner.RefitFor<T>()` instead. Do not add a non-throwing `Try*` twin to
  `OwnerAdmin`; roughly half the admin surface would need one.
- `OwnerAdmin` earns a new method only when two or more fixtures need it *as arrange*. A
  one-fixture need goes through `RefitFor<T>()`.

**Identity**
- Prefer the fixture default. The V1 originals pinned identities because `WebScaffold` shared them
  process-wide; here every fixture boots its own host, so the name is usually arbitrary.
- Need a specific acting identity? Override `PrimaryIdentity`. Do **not** convey it by ordering
  `HostIdentities` — that fails silently, because the identities are structurally identical and the
  tests usually still pass while exercising the wrong one.
- Only list an identity in `HostIdentities` if the server actually resolves it. An identity that is
  merely *named* in metadata costs a tenant materialisation plus a reset per test for nothing.

**Assertions**
- `Assert.That(actual, Is.EqualTo(expected))` — note the argument order flips from
  `ClassicAssert.AreEqual`.
- Never pass a precomputed `bool` to `Assert.That`; pass the value and a constraint
  (`Does.Contain`, `Is.EquivalentTo`, `Has.Exactly(1).Matches(...)`, `Is.Empty`, `Is.LessThan`), so
  a failure prints the value instead of `Expected: True`.
- Drop messages that restate the comparison — NUnit prints both sides. Keep messages that name
  something it can't, such as which loop iteration failed.
- `UnixTimeUtc` is not `IComparable`: `Is.GreaterThan` compiles and throws at run time. Compare
  `.milliseconds`.
- Recurring drive assertions live in `Api/DriveAsserts.cs`. Add to it rather than inlining a fourth
  copy.

**Things that bite**
- `SetupCallerWithOwner` creates the drive *and* builds the caller in one step. If the original did
  anything between those two points, it now happens after. This has been hit in four fixtures so
  far and been inert each time — but check, and record the verdict in the fixture's `<remarks>` so
  a reviewer can tell a checked port from an unchecked one.
- Convert a trailing `if (expected == OK) { … }` to an early `if (expected != OK) return;` — unless
  a statement after the block has to run for every row (a cleanup `Delete`, say). Check first.
- Don't seed for rows that early-return. Guest and no-permission App rows are refused at authz
  before anything reads the drive, so uploads for those rows are wasted; gate the seed on
  `expected == HttpStatusCode.OK`.
- A port is a move, not a rewrite. Carry `[Ignore]`s over verbatim. If you find an assertion that
  never ran or a test that doesn't test its own name, leave the behaviour alone and say so in the
  commit message — three such defects have surfaced this way already.

**Naming**
- The `V1` class-name prefix means only "a fixture from `_V2/` already owns this class name in this
  folder". It says nothing about which endpoints the fixture calls — `UpdateBatchTests` drives V1
  endpoints and has no prefix. Don't infer the rule from neighbours.

## Where things live

```
Hosting/    OdinHost           ← TestServer + tenant container + snapshot/reset
            OdinHost.Snapshots ← DB / payload / queue / cache reset
            OdinHost.TestSync  ← ITestSync resolver (outbox drain)
            InProcessApiClientFactory ← Owner/App/Guest client → server
            DbSnapshot         ← per-tenant identity.db backup/restore
            TestServerHolder   ← bootstrap-time TestServer indirection
            TestSync           ← outbox drain (direct service calls)
            HttpInboxSync<T>   ← caller-scoped inbox processing via HTTP
            OwnerSync, AppSync ← owner / app inbox endpoints
Api/        V2Fixture          ← (in parent dir) the base class
            OwnerSession, AppSession, GuestSession, IV2Caller
            CallerSpec, DriveSpec
            OwnerAdmin (+ .Apps / .YouAuth partials) ← V1 admin endpoints
            DriveHandles       ← reader + writer + reactions, bundled per caller
            Identities         ← Frodo/Sam/… constants (derived from TestIdentities)
Auth/       OwnerLogin         ← ECC + AES-CBC password-set + authenticate dance
Peer/       PeerFlow           ← drive-create + circle + connect helper (+ bidirectional)
            TestPeerHttpClientFactory  ← server-to-server in-process routing
            TestPeerCapiAuthenticationHandler ← test-side peer auth (X-Test-Peer-Identity)
            FrodoToSamPeerTransferTests, PeerScenarioTests
Isolation/  PerTestResetTests  ← proves per-test reset isolates state
            SyncHooksTests     ← proves drain hooks + AppSync resolve
Ported/     Tests ported from _V2/Tests/, organized by concern:
            Auth/, Ping/, DriveRead/, DriveWrite/, LocalAppMetadata/, Reactions/.
            (Peer/ Connections/ Cdn/ folders pre-exist for the later phases.)
            Will rename to flat topical folders under the framework root once all
            phases land and "Ported" stops being a useful distinction.
Smoke/      Ping + multi-tenant routing smokes (ResetBetweenTests = false)
```

The non-obvious *why* lives in XML doc on each class. This README only repeats what's needed for orientation; everything else stays in code.
