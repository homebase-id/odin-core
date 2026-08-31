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

## `Odin.Hosting.Tests.V2.Ported.Drive.DriveReaderTests.InboxDrainOnQueryTests`

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

## `Odin.Hosting.Tests.V2` `DeleteFileIdBatch_*` (whole suite)

**Where:** CI, `windows/sqlite/debug` (seen on run 33402810684, PR #1696, 2026-08-31).
**Shape:** every `DeleteFileIdBatch_*` test fails at a uniform ~12 s - a fixture-wide
peer-setup timeout, not individual assertions.
**Pre-existing evidence:** the same job passed on main at the identical base commit
(run 33381342370, the #1694 merge, ~4 h earlier), both Linux legs passed with the same
change, and the PR touched only SPA static-mount cache headers (no API paths).
A `--failed` rerun of the job passed with no code change.
