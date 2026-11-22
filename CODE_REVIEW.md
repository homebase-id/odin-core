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

A review that reports its cap ran out of room, not out of findings. When `Review-Found` equals `Review-Max`,
fix what it found, commit, and run another round (`Review-Round: 2`) with the same prompt. Stop when a round
reports fewer than its cap, or fixes none of what it found because everything left is kept on purpose; its
commit body says which.

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
Code-Lines: <scripts/pass-stats.py>
```

- **`Found`** is every finding the round reported. **`Fixed`** is how many were fixed. Every other one is in the
  body with its reason: out of scope, a design change to file, a false positive, kept on purpose.
- **`Code-Lines`** is measured, never counted by hand: run `scripts/pass-stats.py` on the staged changes just
  before committing and paste its line.
- **The PR description** repeats each round's counts, with what was fixed and what was skipped.
