# Simplify

The first of a PR's two passes (CLAUDE.md, "Simplify, then code review"). It makes the changed code simpler
without changing what it does, so the code review then reads the cleaned-up diff. Start it with exactly the
prompt in CLAUDE.md, and read this before it runs.

## What it looks for

The `/simplify` skill's four angles: reuse, simplification, efficiency and altitude. Not correctness bugs:
those are the review's.

In this repo, in particular:

- **A helper that already exists**, especially in `Odin.Core` and the services' shared types (CLAUDE.md,
  "Architecture" and "Key Types").
- **DI and scopes.** A new registration or lifetime where an existing one would do (CLAUDE.md, "DI
  Registration"); DB work fanned out across one scoped connection ("Database").
- **Work on a hot path.** A request, a peer call or a drive query that does extra round trips or reads.

A finding that is a design change rather than a cleanup is filed, not added to the PR.

## What it may fix

A finding may be fixed on the branch when it is:

- in code the change adds or touches; or
- a cleanup anywhere that keeps behaviour the same: merging a near-copy, deleting dead code, calling a helper
  that already exists. Touching other parts of the code this way is welcome; or
- a change to tests anywhere, as long as no coverage is lost of how the system is still supposed to work: a
  test may be fixed, isolated, merged, moved or rewritten, but what it checked stays checked.

A fix that changes how a flow the change did not set out to change behaves is out of scope, however real the
finding (password reset, recovery, login, a peer's or another app's access). List it with the finding instead, and file
it if the person who asked for the work agrees. The test before fixing: name the flow whose behaviour the fix
changes. If the PR's description, or the task, doesn't name it, don't fix it: ask.

When new code seems to break a rule written for an older flow, ask whether the rule should cover the new code
before changing either.

## The commit

The pass ends in one commit, even when it changes nothing (`git commit --allow-empty`). A SIMPLIFY commit keeps
behaviour: a behaviour change in it is either a bug or something its body calls out.

```
SIMPLIFY: <what the cleanup did, or "nothing to change">

<each finding fixed, and each one skipped with why>

Simplify-Found: <findings, after merging the agents' duplicates>
Simplify-Fixed: <how many of them were fixed>
Code-Lines: <scripts/pass-stats.py>
```

- **`Found`** counts each distinct finding once, however many agents reported it. **`Fixed`** counts the ones
  fixed; every other one is in the body with its reason.
- **`Code-Lines`** is measured, never counted by hand: run `scripts/pass-stats.py` on the staged changes just
  before committing and paste its line.
- **The PR description** repeats the counts, with what was fixed and what was skipped.
