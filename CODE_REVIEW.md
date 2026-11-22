# Code review

The second of a PR's two passes (CLAUDE.md, "Simplify, then code review"), after the simplify pass's commit.
Start it with exactly the prompt in CLAUDE.md, and read this before it runs.

## What it checks

Correctness first: what the diff gets wrong, with a concrete failure. Then the diff against this repo's rules,
each in CLAUDE.md under the section named, and reported like a bug when broken:

- **"Security model":** a permission check added, changed or centralized without following
  `docs/security-model.md`; a caller reaching data its key or circle doesn't grant.
- **"Multi-Tenancy", "Database", "Drives & File System" and "Peer-to-Peer":** one tenant's data reachable from
  another; parallel work on one scoped connection; a drive or peer change against the rules there.
- **"Authentication & API Versions":** an endpoint on the wrong auth path, or a V1 change a V2 caller needs too.
- **"Cryptography":** keys, IVs or envelope encryption handled outside the existing types.
- **"Opening a PR":** one problem per PR, and the PR facts block.
- **"Reporting findings":** each finding says what was verified by reading code or tests, and what is assumed.
- **Tests:** what a changed or removed test checked is still checked somewhere.

## What it may fix

The same as the simplify pass: SIMPLIFY.md, "What it may fix". A finding outside that scope is listed and asked
about, not fixed.

## Rounds

After each round, decide whether its fixes need a review of their own, and record it: `Review-Again: yes` or
`no`, with the reason in the commit body. The next round reviews the whole branch again, with the same prompt,
not only the fixes. Stop at the first round that says no.

Another round is needed when:

- **the round reported its cap** (`Review-Found` equals `Review-Max`): it ran out of room, not out of findings.
  `Review-Found` counts new findings only: one an earlier round reported and kept on purpose isn't counted again;
- **a fix went deep:** it changed what a flow does, its ordering or concurrency, a stored or sent format, a
  permission or security check, or error handling a caller relies on;
- **the fixes were large next to the change:** the round's `Code-Lines`, added plus removed, is more than about a
  quarter of the branch's (`scripts/pass-stats.py <base>...HEAD`);
- **a fix landed where no test reaches**, so only a reader would see what it broke.

Otherwise one round is enough: say which of these didn't apply. A round that fixes none of its new findings,
because each is kept on purpose with its reason, is `Review-Again: no` even at its cap.

New feature work after both passes means running both again, on the new work. Merges of main and the passes'
own fixes don't count.

## The commit

Each round ends in one commit, even when it fixes nothing (`git commit --allow-empty`).

```
REVIEW: <what the review fixed, or "nothing to fix">

<each finding fixed, and each one skipped with why>

Review-Round: <1, 2, ...>
Review-Level: <the level it ran at>
Review-Found: <findings reported>
Review-Max: <the most findings it could report>
Review-Fixed: <how many were fixed>
Review-Again: <yes or no: whether the fixes need another round, and why in the body>
Code-Lines: <scripts/pass-stats.py>
```

- **`Found`** is every finding the round reported. **`Fixed`** is how many were fixed. Every other one is in the
  body with its reason: out of scope, a design change to file, a false positive, kept on purpose.
- **`Code-Lines`** is measured, never counted by hand: stage the round's fixes, run `scripts/pass-stats.py`, paste
  its line, and commit with a plain `git commit` (no `-a`, no paths), so the commit holds exactly what was measured.
  `scripts/pass-stats.py HEAD` gives the same line afterwards.
- **The PR description** repeats each round's counts, with what was fixed and what was skipped.
