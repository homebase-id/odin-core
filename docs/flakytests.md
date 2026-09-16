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

## `Odin.Hosting.Tests.OwnerApi.Shamir.ShamirPasswordRecoveryTests`

- `CanEnterAndExitRecoveryMode`

**Where:** CI, `windows/sqlite/debug` (seen on run 32957912470, 2026-08-26).

**Symptom:** expects a `Redirect`, gets `Forbidden`.

**Not caused by the change in flight:** same run and reasoning as the entry above.

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
