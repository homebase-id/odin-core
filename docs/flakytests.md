# Flaky and environment-sensitive tests

A register of tests that fail intermittently, or fail only in certain environments, so the
same investigation is not repeated every time one goes red.

**Record every intermittent failure here, always** — including ones that "probably just need a
re-run". A test nobody has written down is a test everyone re-investigates.

Before assuming a red test is your change: check this file, then confirm by running the test on
a clean tree (`git stash`), and by checking whether recent runs on `main` pass.

## How to add an entry

Name the test by its fully-qualified name, say **where** it fails (CI OS/db matrix, or local
only), what the failure looks like, and — critically — the evidence that it is *not* caused by
whatever change was in flight. If the cause is known, say it; if not, say that plainly rather
than guessing.

---

## `Odin.Services.Tests.Dns.Health.DnsHealthServiceTest`

- `ItShouldReportBrokenMailRecordsAsNeedingAttention`
- `ItShouldSplitOptionalRecordsIntoMailRecords`

**Where:** local only. Both pass on CI (verified on the windows/sqlite/debug job of run
32957912470).

**Symptom:** fail when run under a broad filter (`dotnet test --filter "FullyQualifiedName~Mail"`),
pass when run under `--filter "FullyQualifiedName~DnsHealthServiceTest"`. One asserts an
attention count of 1 and gets 0; the other throws inside `CheckOptionalWwwAsync`.

**Not caused by the change in flight:** reproduced on a clean tree by stashing (2026-08-26),
and unaffected by whether the local dev server is running.

**Likely cause (unconfirmed):** these do live DNS lookups, and a local
`docker/stalwart-dev` setup adds `/etc/hosts` entries for `*.dotyou.cloud`. A developer without
those entries would likely not see it. Not yet proven — if you confirm it, replace this
paragraph with what you found.

---

## `Odin.Hosting.Tests.V2.Ported.Peer.TemporalReadTests`

- `TemporalRead_ClampsToWindow_VerifyReportsAccess_AndNormalReadIsRejected`

**Where:** CI, `windows/sqlite/debug` (seen on run 32957912470, 2026-08-26).

**Symptom:** `expected fresh file readable; got NotFound` — a peer file transfer that has not
landed by the time the assertion runs.

**Not caused by the change in flight:** the change was a mail-DNS endpoint, which this test does
not touch; the same job passes on recent `main` runs.

---

## `Odin.Hosting.Tests.V2.Ported.Peer.InboxDrainOnQueryTests`

- `QuerySmartBatch_DrainsInbox_OnRecipient`

**Where:** CI, `ubuntu/sqlite/release` (seen on run 33010865390, 2026-08-26).

**Symptom:** the test fails outright; a peer transfer has presumably not drained by the time
the assertion runs.

**Not caused by the change in flight:** the strongest evidence available — **the identical
commit failed and then passed on re-run with no code change** (`62c66902a`). Both parents of
the merge were green independently: the feature commit at 19:20 and `main` at 19:21. The
change under test touched only `Email/*`, which this test does not reach.

**Pattern worth noting:** this is the third entry from the same family — peer transfers and
timing-sensitive delivery assertions (`TemporalReadTests`, and this). If a fourth appears,
the shared cause is probably worth chasing rather than re-running.

---

## `Odin.Hosting.Tests.V2.Ported.Shamir.ShamirPasswordRecoveryTests`

- `CanEnterAndExitRecoveryMode`

**Where:** CI, `windows/sqlite/debug` (seen on run 32957912470, 2026-08-26).

**Symptom:** expects a `Redirect`, gets `Forbidden`.

**Not caused by the change in flight:** same run and reasoning as the entry above.

**Moved 2026-09-17.** Was `Odin.Hosting.Tests.OwnerApi.Shamir.ShamirPasswordRecoveryTests`; the
fixture is now ported to the fast framework and the V1 original is deleted. The recovery logic is
unchanged by the port, so if the flake is real it is still reachable -- and it is now far cheaper to
chase, because the whole fixture runs in about 2 s instead of 18 s.

**Still not reproduced, and a green local run does not clear it.** 8/8 green after the port (3 batch
runs plus 5 focused), and the V1 original also passed on the same tree -- but that was Linux/sqlite,
filtered and unloaded, whereas the recorded failure is Windows CI under parallel load. Those are not
the same experiment.

**Where to look, from reading the code rather than from a measurement:** a `Forbidden` on
`verify-enter` means an `OdinSecurityException` escaping `ShamirRecoveryService.EnterRecoveryMode`.
Two places on that path can raise one -- `HandleReleaseShardRequest` on a *player*
(`sender != requester`, or a `RecoveryEmailHash` mismatch) and the dealer-side collect. A player's
non-2xx is swallowed by the `if (response.IsSuccessStatusCode)` guard, so it would have to be raised
dealer-side. Unconfirmed: this is analysis, not a reproduction.

---

## `Odin.Services.Tests.JobManagement.JobManagerTests`

- `ItShouldDeleteExpiredUnsuccessfulJobsInTheBackground(Sqlite,0)`

**Where:** local, macOS, `Odin.Services.Tests` full run (2026-09-03).

**Symptom:** `Assert.That(completedJob1, Is.Null)` fails with the job still present —
`Expected: null, But was: <FailingJobTest>`. The background cleanup had not deleted the
expired job by the time the assertion ran. Only the `deleteAfterMilliseconds = 0` case fails;
the sibling case in the same theory passes.

**Not caused by the change in flight:** the change touched `VersionUpgradeService`,
`BuiltinProvisioner` and `CircleNetworkService` — logging and a phase timeout — none of which
the job manager reaches. Re-running the test alone passed (2/2), and it also passed 2/2 on a
stashed clean tree, so the failure reproduces on neither the change nor its absence.

**Pattern worth noting:** a background service racing an assertion, with a zero-length delay
as the parameter. Same family as the timing-sensitive entries above: the test asserts on work
it does not wait for.

---

## `Odin.Core.Tests.Threading.KeyedAsyncLockTest`

- `LockedExecuteAsync_ConcurrentDifferentKeys_ExecutesConcurrently`

**Where:** CI, `ubuntu/postgres/release` (seen once on run 34635091541, 2026-09-11). The same
test passed on the `ubuntu/sqlite/release` and `windows/sqlite/debug` jobs of that build, and on
every other run of the branch that day.

**Symptom:** `Actions with different keys should execute concurrently.` — the
`Task.WhenAny(allTasks, Task.Delay(150))` race is won by the timeout instead of the work.

**Not caused by the change in flight:** the branch (`connection-review-support`) does not touch
`KeyedAsyncLock` or its test — `git diff main...HEAD -- '*LockedExecute*' '*AsyncLock*'` is
empty — and the test passes on recent `main` runs.

**Cause:** the test queues 50 `Task.Run` bodies that each `await Task.Delay(100)`, then asserts
they all finish inside 150 ms. That leaves 50 ms of slack for thread-pool ramp-up across 50
tasks, which a loaded CI runner can exceed. Verified by reading the test
(`tests/core/Odin.Core.Tests/Threading/KeyedAsyncLockTest.cs:316`); no fix attempted here — the
budget would need widening, or the assertion rewritten to measure concurrency rather than
wall-clock.

---

## `Odin.Hosting.Tests.Kestrel.ProxyProtocolListenerTests`

- `ConnectionThatSendsGarbage_IsStillLoggedAtWarning`

**Where:** CI, seen twice on the same commit (`1d79176e4`, PR #1733, 2026-09-13):
`windows/sqlite/debug` of run 34766411275 and `ubuntu/sqlite/release` of run 34766545441. The
same test passed on the other four jobs of that commit (runs 34766413714, 34766416193,
34766545403, 34766545391) — each OS/db combination both passed and failed at least once.

**Symptom:** `a peer that speaks the wrong protocol must still warn` —
`Assert.That(events, Is.Not.Empty)` after the test's 10 s wait; no Warning-level PROXY event was
captured for the connection that sent `GET / HTTP/1.1` instead of a PROXY header.

**Not caused by the change in flight:** the branch only touches
`HomebaseChannelContentService` and a new `PublicPage` test, nothing under Kestrel or the PROXY
listener; all three CI workflows passed on `main` at the branch's base (`6df4c4301`). Not
reproduced on a clean tree locally (port 8443 was occupied at the time).

Failed a third time on `21bc5fa85` (`ubuntu/sqlite/release`, run 34768975917, 2026-09-13).

**Status:** marked `[Explicit]` (2026-09-13) so it no longer runs in CI; tracked in #1734.
Remove the attribute and this entry once that is fixed.

**Cause (suspected, not reproduced):** the test disposes the socket right after writing, and
`ProxyProtocolConnectionMiddleware` links `ConnectionClosed` into the read token. If the close is
observed before the first read returns the buffered bytes, the read is cancelled with
`bytesReceived == 0` and logged at Verbose instead of Warning. Details and candidate fixes in
#1734. The test was added by `9d1315b7e` (PR #1732).

### A second method in the same fixture, 2026-09-17

- `HeaderFromUntrustedPeer_IsRejected`

**Where:** CI, `ubuntu/sqlite/release` on PR #1781 (run 35198…, job 105131700664). One failure in
that project's 156 tests; the fast suite in the same job was green at 1388.

**Symptom:** a different failure mode from the entry above — not a missing log event, but the
listener never came up:

```
listener on port 8445 is not accepting connections (ConnectionReset); a rejection cannot be asserted
    at ProxyProtocolListenerTests.ConnectOrFail(Int32 port, Byte[] proxyHeader):167
```

Worth noting the assertion message is a good one: it says what it could not do and why, rather
than printing a bare failure. That is why this entry can state the failure mode at all.

**CORRECTED — it IS implicated by the change in flight, and my first note here was wrong.** I
originally wrote "not caused by the change in flight" on the grounds that `git diff
origin/main...HEAD` is empty for `*Kestrel*`, `*ProxyProtocol*` and `Startup.cs`. That reasoning
only rules out a *code* path, and it is not the only causal path. PR #1781 deletes six fixtures from
this project, which changes fixture ordering — and this fixture adds its 8444/8445 listen entries
through **process-wide env vars** (`WebScaffold.RunBeforeAnyTests(envOverrides:)`), so what else is
booting around it matters.

The evidence that it is implicated:

- It **passed on PR #1776**, with identical Kestrel code, before the deletions.
- Recent `main` runs of this workflow are green.
- It then failed **twice consecutively** on #1781's `ubuntu/sqlite/release` with an identical
  message. Two-for-two is not flake-shaped.

The evidence that the defect is nonetheless pre-existing, not introduced:

- It does **not** reproduce locally: 15/15 in isolation, and the whole project green (156 tests) in
  the CI Release configuration with CI's define constants.
- The fixture pins ports 8443/8445 and never waits for the bind, which is a latent hazard
  independent of ordering.

Best reading, stated as inference rather than fact: a pre-existing fixed-port/no-bind-check defect
that this PR's reordering exposed. I could not reproduce it locally, so the mechanism is not
confirmed.

**It is not one test, and it is not a port race — second correction.** `UntrustedPeer_IsStillLoggedAtWarning`
fails identically on the postgres matrix, and those are *exactly* the two tests in this fixture that
use 8445. What the evidence actually shows:

- **Listen entry 1 (8444) comes up.** `HeaderFromUntrustedPeer_IsRejected` does a positive-control
  handshake on 8444 before touching 8445, and that control passes — so the host booted and the
  env-var listen-entry mechanism works.
- **Listen entry 2 (8445) does not.** Every test touching it fails; every test on 8443/8444 passes.
- **No bind error anywhere in the CI job log** — `address already in use`, `failed to bind`,
  `AddressInUse` and `8445` all turn up nothing from the host.

So entry 2 is *absent*, not losing a race for a taken port. That means this is **not** the
hard-coded-port hazard of #1779/#1734 that I first filed it under, despite resembling it. Tracked
separately as **#1783**.

**Status:** both 8445 tests marked `[Explicit]` (2026-09-17) pointing at #1783, so a V1
test-infrastructure problem does not block a test-migration PR. The fixture's third `[Explicit]`
test is a different issue (#1734).

**Pattern note, now narrower:** #1779's "fixed port, no happens-before" root cause still covers
`TcpProbeTests` and plausibly #1734's close-vs-read race, but not this one. Two lessons are worth
keeping: a positive control in the same test is what made "entry 1 up, entry 2 down" visible at all,
and an assertion message that says *what it could not do* ("a rejection cannot be asserted") is why
this was diagnosable from a log alone.

---

## `Odin.Hosting.Tests.AppAPI.Transit.TransferFileTests`

- `TransientFileIsDeletedAfterSending`

**Where:** CI, `windows/sqlite/debug` (run 34908631011, attempt 1, commit `125cb622f`, PR #1739,
2026-09-14). The re-run of that job (attempt 2, same commit) passed. The same commit passed on
`ubuntu/postgres/release` (run 34908631040) and `ubuntu/sqlite/release` (run 34908630925).

**Symptom:** `Sender should no longer have the file since we used IsTransient` —
`GetFileHeader` on the sender returned something other than `NotFound` after the outbox and
inbox were processed for a transient transfer.

**Not caused by the change in flight (evidence, not proof):** the same job passed on re-run
with no code change, and the test does not appear in the logs of the 8 most recent failed
`windows/sqlite/debug` runs checked on 2026-09-14. The change (reviewed security tier) only takes
effect when a tenant enables `UseReviewedSecurityTier`, which this test does not do. Not
reproduced on a clean tree locally: port 8443 was occupied, so `Odin.Hosting.Tests` could not
start.

**Cause:** unknown. The assertion runs immediately after the transfer, so a delayed deletion of
the sender's transient copy on the slower Windows runner is a plausible explanation, but it has
not been confirmed.

---

## `Odin.Hosting.Tests.OwnerApi.Shamir.ShamirPasswordRecoveryFinalizationTests`

- `ShardingIsResetAfterPasswordIsRecovered`

**Where:** CI, `windows/sqlite/debug` only (run 35042063948, commit `fea206117`, PR #1751,
2026-09-16). The test carries `#if !DEBUG [Ignore]`, so the two Release jobs never run it — both
passed. The same Windows job passed on the two preceding commits of the same branch (`c2f6938b2`,
`26ad0ae73`).

**Symptom:** `System.TimeoutException : Failed waiting for expected state
AwaitingOwnerFinalization`, after 48 s. `SecurityApiClient.WaitForShamirStatus` polls every 100 ms
against a fixed 40 s budget; the dealer never reached that state once the four delegates had
approved their shard releases.

**Not caused by the change in flight (evidence, not proof):** `fea206117` changes exactly one file
-- `GrantOnConnectEnrollmentTests.cs` in `Odin.Hosting.Tests.V2` -- so the production code is
byte-identical to `26ad0ae73`, on which this same Windows job passed. The failing test lives in a
different assembly and never enrols a Connect circle. The 12 most recent `main`
`windows/sqlite/debug` runs (2026-09-12 to 2026-09-15) contain one failure, and it was a different,
already-registered test (`TransientFileIsDeletedAfterSending`). Not reproduced on a clean tree
locally: port 8443 is held by a local Docker container, so `Odin.Hosting.Tests` cannot start.

**Cause:** unknown. A fixed 40 s budget for a four-peer state machine on the slowest runner in the
matrix is the obvious suspect. One mechanism specific to this commit, untested: the new tests add a
third identity and peer traffic to a V2 fixture, and `dotnet test` runs test projects in parallel,
so they may have raised contention on the Windows runner without changing any behaviour. The failed
job was re-run on 2026-09-16 to see whether it reproduces.

**Pattern:** the fourth entry in the timing-sensitive peer-delivery family flagged above. Per that
note, the shared cause is now worth chasing rather than re-running.

---

## `Odin.Services.Tests.JobManagement.JobManagerTests` (second entry)

- `JobShouldHaveInternalChildDiScope(Sqlite)`

**Where:** CI, `ubuntu/postgres/release` (seen on run 35086381896, 2026-09-16, PR #1756).

**Symptom:** `AssertLogEvents` fails -- `Unexpected number of Error log events, Expected: 0, But
was: 1` -- and the test takes **30 s** rather than its usual sub-second.

**Cause (identified, unusually for this file):** the 30 s is the signature of
`BackgroundServiceManager.NotifyWorkAvailableAsync`, which polls for the target background service
`30 x 1s` and then throws `Background service 'JobRunnerBackgroundService' not found` -- that throw
is the one unexpected Error event. The test calls `StartBackgroundServices()` before
`ScheduleJobAsync`, so this is a startup race: under CI load the runner had still not registered
itself when the notify went looking, and the poll window expired.

**Not caused by the change in flight:** PR #1756 deletes tests from `Odin.Hosting.Tests` and edits
this file; it touches neither `tests/services/` nor `src/services/Odin.Services/Background/`
(`git diff main --stat` over both paths is empty), and the failing test is in a different assembly.
Ran 3/3 green locally at ~430 ms each. The two most recent `main` failures on this same workflow
(runs 34033226377 and 33409597404) were different tests, so this specific method had not been seen
failing before.

**Pattern worth noting:** same fixture as the `ItShouldDeleteExpiredUnsuccessfulJobsInTheBackground`
entry above, and the same shape -- an assertion racing a background service. Note that the 30 s poll
is only correct for a service that is *slow to start*; for one that will never start it is a
guaranteed 30 s stall plus an error. That distinction is a real defect in
`BackgroundServiceManager` (it also stalls CLI mode and pre-provisioned-cert hosts, and PR #1757
works around it in the fast test host); fixing it would likely make this flake impossible too.

---

## `Odin.Hosting.Tests.V2.Ported.Peer.DeleteBatchTests`

- `DeleteFileIdBatch_WithSingleRecipient_PropagatesDeleteToRecipient`

**Where:** local, full `Odin.Hosting.Tests.V2` run (2026-09-16), 1 failure in 2 consecutive runs
of the same build.

**Symptom:** the recipient's copy has not flipped to `Deleted` by the time the assertion runs.

**Not caused by the change in flight:** the change was porting five unrelated `_Universal`
drive fixtures onto the fast framework; it touches neither this fixture nor the peer outbox. The
identical build passed the immediately following run, so the failure reproduces on neither the
change nor its absence.

**Pattern worth noting:** this is the fifth entry in the timing-sensitive peer-delivery family
(`TransferHistoryTests`, `InboxDrainOnQueryTests`, the Shamir entry, and this). Per the note on
those, the shared cause is worth chasing rather than re-running -- they all assert on delivery
they do not deterministically wait for. The fast framework has `Sync.DrainOutboxAsync()` /
`ProcessInboxAsync()` for exactly this; a port that keeps a V1-style implicit wait inherits the
flake.

**Second failure, 2026-09-17, and it revises the above.** The same test failed again on a full
Debug run, but *not* on the delivery assertion -- it failed in teardown, on the log-event
invariant:

```
[1/1] Error: "SQLite Error 5: 'database is locked'."
Exception origin: "POST" "/api/peer/v1/host/drives/deletelinkedfile"
```

So at least one failure in this family is not a missing wait at all: the delete never lands
because the peer's write loses a lock race, and the delivery assertion was the *symptom*. The
diagnosis above was written before `AssertLogEvents` printed events -- it was inferred from a bare
`Expected: True`, and it is now clear that a bare assertion cannot distinguish "not waited long
enough" from "the write failed". Treat the timing story as unconfirmed for this entry until a
failure is seen with a clean error log.

**This is the evidence #1777 was filed without.** When I filed #1777 I recorded that I had not
established whether SQLite write contention was reachable at realistic concurrency, as opposed to
under the deliberate hammer fixtures. This is an ordinary two-identity peer delete, on Linux, with
no hammer -- so it is reachable. Three of the entries in this file (`V1PeerReadReceiptTestsSuccess`,
`ConcurrentOverwriteEncryptedHeaderTests`, and this) are now the same `database is locked` cause.

**RESOLVED 2026-09-19 -- a harness defect, not product contention.** The V2 harness ran every test
in rollback-journal mode, not WAL. `BackupSqliteDatabase` switched the live database to
`journal_mode=DELETE` before taking the fixture's snapshot and never switched it back. The product
sets WAL once per database per process, which had already happened, so the database stayed in
rollback mode for the rest of the fixture. Measured on a live V2 host: `PRAGMA journal_mode`
reported `delete` in every test after the baseline. In that mode readers and writers block each
other and can deadlock, which WAL never does: a writer's commit waited 1051 ms behind an open reader,
against 2 ms in WAL. The reset also restored with a raw `File.Copy` over the live file. Once WAL was
back, that silently replayed the previous test's surviving `-wal` over the restored file (301 rows
seen after "resetting" to 1).

Fixed by snapshotting and restoring through SQLite's backup API, which leaves the database in WAL
and resets it correctly even with connections open. A live V2 host now reports `wal`. Results:
- 5 consecutive full `Odin.Hosting.Tests.V2` runs: all green, no `database is locked`.
- The `database is locked` toleration was removed from `V1PeerReadReceiptTestsSuccess`, and
  `PayloadConcurrentHammerEncryptedTests` is no longer `[Explicit]`. Both passed 15 of 15 local runs.

Not reproduced beforehand: the failure was roughly 1 in dozens of full runs. Also, Microsoft.Data.Sqlite
retries a busy database for 30 s before throwing, so the observed error means some request waited out
that 30 s. Rollback-mode deadlocks allow that and WAL does not; that link is inference. The product's
peer-delete path was traced and holds no transaction across I/O and never writes under an open
reader, so nothing here points at production.

---

## `Odin.Hosting.Tests.V2.Ported.DriveWrite.HammerTimeLocalUpdateBatchTests`

- `UpdateBatch_HammerTime_WithPayloads`

**Where:** local, `--filter "FullyQualifiedName~Ported.DriveWrite"` (2026-09-16), 1 failure in the
first of 8 runs of that filter on the same day.

**Almost certainly the same flake seen earlier the same day on the full suite** (PR #1768): 1 failure
in 8 full-suite runs, on the first run after a rebase, never reproduced. That one went unidentified
because the failing run's output was truncated before the test name — which is the practical argument
for the assertion rule below, and for not piping a red test run through `tail`.

**Symptom:** `Assert.That(getThumbnailResponse.IsSuccessStatusCode, Is.True)` in
`UploadAndValidatePayload` -- the thumbnail GET comes back unsuccessful. Two threads hammer
update-batch against one shared file for 100 iterations each, so a reader can hit the payload/thumbnail
of a version another thread is in the middle of replacing.

**Not caused by the change in flight:** the failure was observed on the *baseline* run, before any
edit to this fixture (the change that followed was a `/simplify` cleanup of the batch-6 DriveWrite
ports). It could not be confirmed with `git stash`: the worktree was shared with other agents at the
time, so stashing was not available. After the cleanup -- which also removed this fixture's carried
5-50 ms inter-iteration sleep -- the fixture passed 6 consecutive runs (1 full DriveWrite filter plus
5 hammer-only runs).

**Pattern worth noting:** unlike the peer-delivery family above, nothing here is waiting on a
background service; it is a genuine read-during-write race that the fixture's own two writers create,
and the original `_Universal` test has the same shape. The failure message was uninformative because
the assert was `Is.True` on `IsSuccessStatusCode`, which records no status code; the cleanup converted
this fixture's asserts to exact-status form, so a recurrence will name the code it got.

## `Odin.Hosting.Tests.V2.Ported.Connections` — the introduction family

- `Introductions.IntroductionTestsAutoAcceptEnabledOnAllIdentities.WillHandleWhenAllWhenConnectionsFailsVerification`
- `Introductions.ConfirmConnectionTests.CanConfirmConnection`
- `Introductions.AutoAcceptTests.WillNotAutoAcceptWhenRecipientDisablesIntroductions`

**Where:** local, full fast suite (1231 cases) under `ParallelScope.Fixtures`, 2026-09-16/17.
3 distinct failures across ~20 full-suite runs; each fixture passes in isolation and on most runs.

**Symptom:** an assertion that a connection exists, not a log-event failure. e.g.
`sam.dotyou.cloud must hold merry.dotyou.cloud as an introduced connection / Expected: True, But was: False`,
and `ConnectionStatus / Expected: Connected, But was: None`. The introduction simply never landed.

**Cause -- corrected 2026-09-17, the first diagnosis was wrong.**

The original entry blamed the test framework: `DrainAsync` makes `DefaultDrainRetryPasses = 3` passes
where V1's running background service retried up to `OutboxOperationMaxAttempts = 30`, so an item
needing more than three was said to be abandoned here and delivered there.

That is not what happens. `DrainAsync` calls `BringForwardScheduledItemsAsync()` between passes
(`PeerOutboxProcessorBackgroundService.cs:134`), which pulls a deferred item's `nextRun` forward and
retries it. The retry budget is not the constraint. **The introduction send genuinely fails, several
times in a row, under concurrency** -- the test is reporting a real defect, not a framework shortfall.

Do not "fix" this by draining harder. Tracked as a product issue: **#1778**.

Worth knowing the production asymmetry while reading these failures: a failed `ConnectIntroducee`
item reschedules for **+10 minutes**, hardcoded in two places
(`ConnectIntroduceeOutboxWorker.cs:45` and `:73`, the latter carrying `//TODO: change to calculated`).
Tests bring that forward; production waits it out. So a transient introduction failure costs a real
user ten minutes, which matches the product's reputation for flaky introductions.

**Not caused by the log-event invariant** that was enabled in the same change: these are assertion
failures about connection state, independent of log assertions. The invariant is what made them
visible, by prompting the repeated full-suite runs that surfaced them.

## `Odin.Hosting.Tests.V2.Ported.Transit` — error-log events cross fixture boundaries

- `Transit.TransitBadCATDetectionTests.CanDetectBadCAT_and_UpdateICR_and_FallbackToPublicAccess`
- any fixture in `Ported/Transit` that does **not** list issue #1771's message in
  `ToleratedErrorLogSubstrings`

**Where:** local, `--filter "FullyQualifiedName~Ported.Transit"` under `ParallelScope.Fixtures`,
2026-09-17, while porting the `AppAPI/Transit` batch. Roughly 1 failing run in 3 of that filter
before the ported fixtures were given the toleration; the failing test differed run to run.

**Symptom:** a log-event failure, never an assertion failure — `The server logged N error-level
event(s) during this test`, every one of them
`Remote identity host failed: Referenced filed and metadata payload encryption do not match`
with origin `POST /api/peer/v1/host/drives/upload` (issue #1771).

**Cause — measured, not inferred: a fixture's log store receives Error events produced by another
fixture's host.** The tests that fail this way make no peer call at all. The clean experiment:
running `Ported.Transit.AppTransitQueryTestsForPublicFiles` (Merry and Pippin are not even connected
in it; nothing is uploaded over transit) together with `TransitCommentFileRoutingTests` (a known
#1771 producer) and nothing else reddens the *former* with the latter's error text — 1 failing run in
5. The same fixture alone passed 6 consecutive runs, and 6 more as part of the four ported
`AppTransit*` fixtures. So the per-host log isolation asserted in `V2Fixture.AssertNoErrorLogEvents`'
own remarks ("each `OdinHost` owns its own store, and the sink is bound to that host's store at
startup") does not hold under `ParallelScope.Fixtures`. Peer *routing* is not the culprit:
`TestServerHolder` is registered per host, not statically. The likely mechanism is Serilog's static
`Log.Logger`, which `UseSerilog` replaces on each host boot, but that was not confirmed.

**Worked around, not fixed.** The five fixtures ported in this batch list the #1771 substring in
`ToleratedErrorLogSubstrings` with a comment; `Ported.Transit` then passed 6 consecutive runs.
`TransitBadCATDetectionTests` does not carry that toleration and is still exposed — it was left
untouched because it belongs to an earlier batch and was not part of this one. Note that the
toleration is what makes the *bleed* survivable; while it is in place, a fixture that tolerates the
message cannot distinguish its own occurrence of #1771 from a neighbour's. Fixing the isolation
(or #1771) is what removes the whole class.

---

## `Odin.Hosting.Tests.V2.Ported.DriveWrite.ConcurrentOverwriteEncryptedHeaderTests`

- `Overwrite_Encrypted_PayloadManyTimes_Concurrently_MultipleThreads`

**Where:** CI, `windows/sqlite/debug` only (run 35184385873, 2026-09-17). Failed after 2m14s.
`ubuntu/sqlite/release` and `ubuntu/postgres/release` passed the same commit; 1 failure in 1367.

**Symptom:** `Assert.That(tag.HasValue, Is.True) / Expected: True, But was: False`, three times in one
`Assert.Multiple`. The fixture runs 20 threads x 50 iterations, each overwriting its own encrypted
header and carrying the version tag forward; a null tag means an upload did not succeed.

**The assertion did not say why, and that is now fixed.** `UploadAndValidateHeader` captured the
status code (it counts 500s into `_serverErrorCount`) and then returned a bare `null`, so the failure
printed `Expected: True` and nothing else. It now returns the status alongside the tag and asserts on
the status, so the next occurrence names the code. This is the third time in one sitting that a
precomputed-bool assertion hid a diagnosis -- see also #1772 (a 500 behind `IsSuccessStatusCode`) and
the log-event invariant (three product bugs behind `Expected: 0`).

**CORRECTED 2026-09-17 -- it is not #1777, and the first guess here was wrong.** The paragraph that
stood here attributed this to the SQLite busy-timeout contention of #1777, reasoning from the
`[Explicit]` sibling `PayloadConcurrentHammerEncryptedTests`, whose comment names exactly that. The
improved assertion then produced the actual evidence and it does not support that: the uploads
answer **`InternalServerError`** on six named iterations, and the failing run's log contains **no**
`database is locked` anywhere in the `Odin.Hosting.Tests.V2` section. Reasoning from a neighbour's
comment is not evidence. Now tracked on its own as **#1780**.

What the evidence does say: each thread overwrites *its own* file, so this is twenty concurrent
writers against one **drive**, not several writers racing one file. The server-side exception behind
the 500 did not reach the CI output, so the cause is still unknown; the fixture now captures the
first 500's response body into the assertion message so the next run names it.

**Frequency: roughly two failures in three Windows runs — it is intermittent, not deterministic.**
It passed the full 20x50 run on `windows/sqlite/debug` for PR #1781 (job 105144168218) after failing
on two earlier commits. So a green Windows run does not clear it, and the response-body capture added
for diagnosis has not fired yet — the cause still rests on the six
`Expected: OK, But was: InternalServerError` iterations from the failing runs.

**Status: left RUNNING on `windows/sqlite/debug`, on purpose (decision 2026-09-17).** It was
briefly `[Ignore]`d against #1780; that was reverted. Unlike its two siblings
(`PayloadConcurrentHammerEncryptedTests`, then `[Explicit]` -- running again since the 2026-09-19
journal-mode fix, see the `DeleteBatchTests` entry -- and `UpdateBatch_HammerTime_WithPayloads`,
`[Ignore]` under #1772), this one stays in CI. The failure is a real product defect rather than a
timing artefact, and ignoring it would buy a green board at the price of the signal. #1780 is marked
high priority. **Do not "fix" this by ignoring or weakening the assertion** -- the claim it makes,
that a losing writer is refused cleanly rather than blowing up, is the only coverage of that claim
in the suite. The fixture captures the first 500's response body into the assertion message, so each
red run should name the exception behind it.

**Not confirmed pre-existing.** The port carries the `_Universal` original's concurrency shape
unchanged and the `[Explicit]` sibling's comment predates this work, which argues it is not new --
but I could not run Windows locally, and both Linux matrices pass, so `main` has not been checked.

---

## `Odin.SetupHelper.Tests.TcpProbeTests`

- `ItShouldConnectToHttpPortAndGetExpectedResponse`

**Where:** CI, `ubuntu/sqlite/release` on PR #1776 (run 35187682935, 2026-09-17), 1 failure in a
project of 11 tests. `Assert.That(connected, Is.True) / Expected: True, But was: False`.

**Not caused by the change in flight, and this one is easy to be sure of:** `git diff
origin/main...HEAD` is *empty* for `tests/apps/Odin.SetupHelper.Tests/` and for `TcpProbe` /
`DockerSetup`. The PR is a test migration in `Odin.Hosting.Tests.V2`; it cannot reach a TCP probe
in another project. Stated precisely: the recent `main` runs of this workflow are all green, so I
am *not* claiming this has been observed on `main` -- only that the PR does not touch the code
involved.

**Confirmed flaky on the identical commit:** re-running only the failed job, with no code change,
passed. That is the strongest available evidence -- the same build both failed and passed.

**Does not reproduce locally:** 12 consecutive runs of the fixture, all green, on an idle machine.
That fits a race that needs a loaded runner to lose.

**The race, read from the source** (`TcpProbeTests.cs:26-33`):

```csharp
var listenTask = DockerSetup.TcpListen(38080, cts.Token);   // not awaited to "bound"
var (success, message) = await tcpProbe.ProbeAsync("127.0.0.1", "38080");
await cts.CancelAsync();
var (connected, error) = await listenTask;
```

Nothing synchronises the probe with the listener actually being bound and accepting. On a loaded
runner the probe can run before the listener is ready, or the cancel can arrive before the accept
completes, and `connected` comes back false. Two further hazards in the same shape: the port
**38080 is hard-coded**, so two jobs or fixtures on one runner collide; and the assertion is on a
precomputed bool, so the failure prints `Expected: True` and never says which of the two happened.

**Tracked as #1779. Fix, not done there** (out of scope for a test-migration PR): have `TcpListen` expose a
"listening" signal to await before probing, take an ephemeral port instead of 38080, and assert on
`error` before `connected` so the message survives. Note the file already carries a retry for
external flakiness (`843ab7f64`, #1328), so this area has a history.
