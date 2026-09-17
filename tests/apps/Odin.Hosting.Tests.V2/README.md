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
| Wall clock (full V2 suite) | a couple of minutes, and roughly flat as tests are added | ~20 min, and grows linearly |

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

- **V1 controller tests are no longer a non-goal.** V1 endpoints work here: the V1-shaped Refit clients from `_Universal` are reused as-is, and `InProcessApiClientFactory` resolves their relative paths against the caller's own V1 base (`/api/owner/v1`, `/api/apps/v1`, `/api/guest/v1`) so Owner/App/Guest permission matrices behave as they do on Kestrel. See **Porting rules**.
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
- Unauthenticated callers use `Host.CreateAnonymousClient(identity)` (`Api/AnonymousHttp.cs`) — the
  counterpart of V1's `WebScaffold.CreateAnonymousApiHttpClient`. It is `Host.CreateClient()` plus a
  tenant `BaseAddress` and the file-system-type header, so well-known / SSR / swagger GETs and
  `RestService.For<T>` against an anonymous surface both work. Bare `Host.CreateClient()` stays
  correct where the test spells out an absolute URL (`Ported/Ping`).
- **A `_Universal` context that grants permission *keys only* has no `CallerSpec` equivalent.**
  `AppPermissionsKeysOnly` and `ConnectedIdentityLoggedInOnGuestApi` register an app / YouAuth domain
  with a `PermissionSet` and *no* `Drives` at all, whereas `CallerSpec.App` / `.Guest` always attach a
  `DriveGrantRequest`. Porting one through `CallerSpec` therefore either invents a drive or grants
  access the original didn't. `Ported/Concepts/CollabScenario.cs` shows the shape: a local
  `record`-based spec carrying a `Func<OwnerSession, Task<IV2Caller>>`, named for the V1 context so
  failures read the same.

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
  anything between those two points, it now happens after. It has been inert every time so far —
  but check, and record the verdict in the fixture's `<remarks>` so a reviewer can tell a checked
  port from an unchecked one.
- Convert a trailing `if (expected == OK) { … }` to an early `if (expected != OK) return;` — unless
  a statement after the block has to run for every row (a cleanup `Delete`, say). Check first.
- Don't seed for rows that early-return. Guest and no-permission App rows are usually refused at
  authz before anything reads the drive, so uploads for those rows are wasted; gate the seed on
  `expected == HttpStatusCode.OK`.
  **Verify it per endpoint rather than assuming it** — update-batch is a measured exception. There,
  an update with no `VersionTag` answers 400 for *every* caller (validation precedes authz), and a
  `Guest[Write]` row clears the drive check and is refused deep enough in that a non-existent file
  answers 500. Those rows still need a local seed; what they don't need is the peer arrange
  (recipient logins, drives, connection handshakes), which is where the time actually goes.
- **Every passive poll becomes an explicit drain.** `DriveRedux.WaitForEmptyOutbox`,
  `Connections.AwaitIntroductionsProcessing` (the same poll of the transient-temp-drive outbox under
  another name) and `DriveRedux.ProcessInbox` all wait on the outbox background service, which the
  fast host registers but never starts — leaving one in hangs for its full timeout and then throws.
  They become `owner.Sync.DrainOutboxAsync()` / `owner.Sync.ProcessInboxAsync(drive)`, or
  `PeerFlow.DistributeAsync` for the pair. On the introduction path this is more than a timing
  change: draining an *introducee's* outbox is what sends the introductory connection request
  (`ConnectIntroduceeOutboxWorker`), so the drain has to go on the introducee, not just the
  introducer.
- A `Task.Delay` standing in for a poll that can never finish — the shape where the test asserts a
  *failed, still-queued* outbox item, so the outbox is never empty — has to become
  `DrainOutboxAsync()` as well, because nothing else moves the items that *do* deliver. **Whether the
  failed item survives the drain is not a property of the drain; it is what the item's worker
  returns.** `ProcessItem` marks an item complete (gone) when the worker says the send is resolved,
  and reschedules it (still queued) when the worker says retry — and `DrainAsync`'s three passes are
  far below `OutboxOperationMaxAttempts`, so a rescheduled item is still there when the drain
  returns. Both shapes are in the suite, each measured:
  - *Still queued* — a recipient who severed the connection answers access-denied, the worker
    reschedules, and `TotalInOutbox` is what the test asserts on.
    `Ported/Peer/V1TransferHistoryMultipleRecipientsTests` relies on this, and is now deterministic
    where the V1 original slept.
  - *Gone* — an introduction to a blocked recipient resolves permanently, so the worker marks it
    complete and the item is absent after the drain (probed either side in
    `Ported/Connections/Introductions/AutoAcceptTests`). For that shape "assert a still-queued failed
    item" is not expressible today; it would need `ITestSync` to surface `maxRetryPasses`.

  So don't assume either outcome: check what the worker for that `OutboxItemType` returns, and record
  the verdict in the fixture's `<remarks>`.
- **A fixture whose subject is initial setup overrides `WarmTenantBaselineAsync`.** The baseline runs
  `Admin.InitializeIdentity()` before the snapshot, so `isconfigured` is already true and "system
  circles do not exist yet" is unreachable. Override it to keep the owner login — that sets the
  password `TakeBaselineAsync` needs — and drop only the `InitializeIdentity` call.
  `Ported/DriveManagement/HandleDriveAddedRegressionTests` and the two
  `Ported/Configuration/SystemInitializeConfig*` fixtures do exactly this. Only do it where the
  pre-init state is actually asserted: the other `SystemInit` ports call `InitializeIdentity` inside
  the test, which is idempotent on the server, so they keep the default baseline.
- **`RunBeforeAnyTests(envOverrides:)` becomes `ConfigOverrides`, and the tear-down that undid it
  goes away.** Env vars are process-wide, so the V1 fixtures that set one had to clear it again or
  leak the flag into every later fixture (`OwnerApi/Mail/MailActivationTests` said so in a comment).
  `ConfigOverrides` is per host, so the cleanup has nothing to do — and the flag-off sibling fixture
  no longer depends on the flag-on one having tidied up first. List settings bind by index
  (`Email:TenantMail:MxNodes:0`), not `__0`.
- **The log-event assertion is ON, and it will catch things your test never looks at.** A test fails
  if the server logged an Error or Fatal during it, even when every explicit assertion passed — the
  invariant `WebScaffold` enforced via `AssertLogEvents`. When a port trips it, **read the message it
  prints** (it renders every event and exception) before deciding what to do:
  - the error is the behaviour under test → add its text to `ToleratedErrorLogSubstrings` on that
    fixture, with a comment saying why. For an outbox delivery that is *meant* to fail, use the shared
    `OutboxDeliveryFailureLogged` constant.
  - it looks like a real defect → file it, and tolerate it with the issue number attached. Three of
    the first four things this caught were product bugs (#1770, #1771, #1772), all in tests whose own
    assertions passed.
  - turn `AssertNoErrorLogEvents` off only if the whole fixture is about error paths.

  Two framework-level lists sit alongside the per-fixture one, and the distinction matters:
  `KnownProductNoise` (filed defects, deleted when fixed) and `ParallelLoadArtefacts` (SQLite lock
  contention from running many hosts in one process — not a defect, not expected to shrink).
- **`Is.EqualTo` across `GuidId` and `Guid` compiles and then fails at run time.** NUnit compares the
  boxed objects and never reaches the `==` operator, so you get
  `Expected: bb2683fa-402a-… / But was: <bb2683fa402aff…>`. Cast *both* sides to `Guid`.
- `FileMetadata.OriginalAuthor` is an `OdinId`; `FileMetadata.SenderOdinId` is a `string`. Comparing
  the first against a `(string)` cast fails with the baffling
  `Expected: "frodo.dotyou.cloud" / But was: frodo.dotyou.cloud`.
- Several endpoints answer **204 NoContent**, not 200: follow / unfollow, `GET /followers/follower`
  for a non-follower, and the peer add/delete-reaction calls. The V1 clients asserted
  `IsSuccessStatusCode`, which hid this; asserting the exact status code surfaces it, which is the
  point of the rule under **Asserting a response**.
- `TestIdentities.InitializedIdentities` is **null** here. Only `WebScaffold.RunBeforeAnyTests` calls
  `TestIdentities.SetCurrent`; `V2Fixture` never does, so anything that reaches an identity through
  that dictionary — looking up `ContactData`, say — throws a `NullReferenceException` at run time.
  Use `TestIdentities.Defaults.Single(i => i.OdinId == identity)` instead.
- **Merging fixtures is allowed only where the difference is already a caller.** Several V1 subtrees
  hold the same tests once per auth scheme (the follower trio was three fixtures, two of them
  byte-identical). Where the *only* difference between them is which client issues the reads, they
  become one fixture with a `CallerSpec` matrix — the `<remarks>` then has to say which original each
  row came from, and any row-specific divergence gets its own column in the case source rather than
  being smoothed away (`Ported/Follower/FollowerTests` does both). A difference in assertions,
  endpoints, or an owner-only capability is not a caller difference: keep those separate, share only
  the arrange. Never merge where coverage would change.
- A port is a move, not a rewrite. Carry `[Ignore]`s over verbatim. If you find an assertion that
  never ran or a test that doesn't test its own name, leave the behaviour alone and say so in the
  commit message. Several such defects have surfaced this way; finding them is a side benefit of
  the migration, but fixing them inside a port makes the diff unreviewable.

**Asserting a response**
- Success and refusal both take the same form: `Assert.That(r.StatusCode, Is.EqualTo(HttpStatusCode.OK))`.
  No message — NUnit prints both codes — and don't also assert `IsSuccessStatusCode`, which is the
  same claim in a form that prints `Expected: True`.
- This is the exception to "never pass a precomputed bool": `IsSuccessStatusCode` is one, and it
  appears hundreds of times in older fixtures. Don't go changing those; do write new ones the form
  above.
- Reading one entity back: prefer the single-entity helper (`owner.Admin.GetCircleDefinition`,
  `GetDrive`) over list-then-`.Single()`. Use the list endpoint only when the list is the SUT.

**Cleanup**
- Trailing `Delete` / `Cleanup` calls that only restore state go away. Per-test reset already
  guarantees it, and "a port is a move" doesn't extend to lifecycle, which `V2Fixture` owns.
  Anything that also *asserts* stays — a delete whose response is checked is a test, not cleanup.

**Refit interfaces**
- `RefitFor<T>` is not limited to `_Universal` interfaces — the `OwnerApi/ApiClient/…` ones are fair
  game and are sometimes the only typed option (the `_Universal` circle interface has no
  `SetCircleOwningApp` at all).
- **Watch out:** `IRefitOwnerCircleDefinition` exists *twice*, under
  `OwnerApi/ApiClient/Membership/Circles` and `_Universal/ApiClient/Owner/CircleMembership`, with
  different members and return types. `owner.RefitFor<IRefitOwnerCircleDefinition>()` reads
  identically either way — only the `using` decides. Check which one you bound to.
- Need an endpoint the interface doesn't declare? Add the declaration to the `_Universal` one. That
  is the surface that survives V1 retirement; its `OwnerApi` twins are being deleted as their last
  consumers go.
- When a fixture's SUT *is* the admin surface, "Admin for arrange, RefitFor for the SUT" collapses to
  RefitFor everywhere. Don't split such a fixture half-and-half — `owner.Admin`'s opinionated
  defaults (metadata, page size, descriptions) silently change the request the original sent.
- Moving an arrange step onto an `OwnerAdmin` helper swaps in those defaults. Check every assertion
  that could read one, and say so in `<remarks>`.

**Stays on WebScaffold**
- Some fixtures can't move: WebSocket-driven, mTLS, or dependent on background-service timers. They
  carry a `FLAGGED:` banner in their class doc saying which blocker and what unblocking would cost.
  `grep -rn FLAGGED tests/apps/Odin.Hosting.Tests/` enumerates them.
- Check the whole folder, not the fixture: the socket usually lives in a sibling helper, so a
  fixture can be socket-bound without naming `ClientWebSocket` itself. `_Universal/AppNotifications/`
  and the LiveRelay fixtures are wholesale out of scope for this reason.

**Keeping this file true**
- Every ported fixture's class doc starts with `Port of <original path>`. Once the original is
  deleted, that line is the only record of what the fixture was, and the only way to answer "has X
  been ported?" without archaeology.
- Record a carried defect in the fixture's `<remarks>`, not only the commit message. The commit
  message is where a reviewer won't look in six months, and it's where this batch's two missed
  defects should have been caught.
- A batch that establishes a new convention, or breaks a stated one, updates this file in the same
  commit. This section exists because three batches' worth of conventions lived as prose on whichever
  fixture happened to invent them.

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
            AppFileUploads     ← the encrypted multipart upload the V1 drive ports arrange with
            AnonymousHttp      ← Host.CreateAnonymousClient(identity): unauthenticated, tenant-bound
            Identities         ← Frodo/Sam/… constants (derived from TestIdentities)
Auth/       OwnerLogin         ← ECC + AES-CBC password-set + authenticate dance
Peer/       PeerFlow           ← drive-create + circle + connect helper (+ bidirectional)
            TestPeerHttpClientFactory  ← server-to-server in-process routing
            TestPeerCapiAuthenticationHandler ← test-side peer auth (X-Test-Peer-Identity)
            FrodoToSamPeerTransferTests, PeerScenarioTests
Isolation/  PerTestResetTests  ← proves per-test reset isolates state
            SyncHooksTests     ← proves drain hooks + AppSync resolve
Ported/     Migrated fixtures, in a folder named for the original's subject. Where two source
            trees collide on a class name, the newer arrival takes a `V1` prefix (see Naming).
            Flattens into the framework root once the migration lands.
Smoke/      Ping + multi-tenant routing smokes (ResetBetweenTests = false)
```

The non-obvious *why* lives in XML doc on each class. This README only repeats what's needed for orientation; everything else stays in code.
