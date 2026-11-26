# Issues

What to do with an issue: reading it, what to write on screen, working on it, opening its PR, and filing
one. The PR rules in CLAUDE.md ("Simplify, then code review" and "Opening a PR") still apply in full; this
adds to them.

## Reading an issue

Before reading code or planning, write the issue's essence on screen: in one or two sentences, the problem as
the person who hit it sees it (what goes wrong, for whom, when), not the fix.

- If the issue holds several unrelated problems, say so, name the one you're taking, and leave the rest for
  their own PRs.
- If, after a first look at the code, you still can't state the essence, or the issue reads two ways, ask.

## Working on it

- **Stay with the problem.** Fix it where it's caused, even when that's far from where it shows: a root-cause
  fix is part of the problem, and a workaround at the symptom is not a good fix. The same bug in other places
  is the same problem, so fix it there too. Small changes in the code you're already touching are fine when
  they make the fix clean, such as a rename, a test, or a caller that has to follow. Code that works and
  isn't part of the problem stays as it is, even if you'd have written it differently. Every change the
  problem didn't need is one more place to break something.
- **Check each change:** can you say in a few words how it serves the essence? If not, leave it out. If what
  you found is really broken, mention it or file it.
- If the work shows the real problem is a different one, say so and restate the essence before going on.

## Opening its PR

Everything in CLAUDE.md, "Opening a PR", applies. On top of it, start the description with two lines:

```
Problem: <the essence from "Reading an issue", updated if the work changed it>
Core change: <file:line> <the essence of the code that fixes it>: why it fixes the Problem
```

The Core change is the heart of the fix, not the whole diff: often one line, sometimes a few lines or a short
block, quoted or named. If the Core change doesn't answer the Problem, the PR solves something else.

After opening the PR, print on screen the PR title followed by its bare full URL, then the Problem and Core
change lines.

## Filing issues

An issue's body reads like a `/plan` prompt: enough context, evidence and scope that someone can open the
repo and plan the fix without a back-and-forth. When the cause isn't known, the issue asks for the test or
the logging that would find it, not for a guessed fix.

### Before filing

- **Search first:** `gh issue list --repo homebase-id/odin-core --state all --search "…"`. Extend or link an
  existing issue rather than filing a duplicate. When two bugs share a cause, say so and link them, so the
  cause is fixed once.
- **This repo is public.** A security problem goes into a draft security advisory on the repo, not an issue.
  Leave out real users' identities (anything but the test identities and the team's own), tokens, keys, and
  whatever else a log line carried that the issue doesn't need.
- **One issue per problem.** Unrelated problems get their own issues.

### Writing it

- **Title:** specific and searchable: the symptom for a bug, the capability for a feature. No trailing period.
- **Ground it, don't overclaim:** give `file:line` refs, but an unproven cause is a hypothesis, and say what
  would confirm it (CLAUDE.md, "Reporting findings").
- **Labels:** `bug` or `enhancement`, plus `priority: high` or `needs-research` when they apply, and
  `post-merge` as below. Don't create a label; ask.
- **Assignee:** whoever the user names (`--assignee <login>`; `gh api repos/homebase-id/odin-core/assignees`
  lists who can be).
- **Create it:** say the title, labels and assignee in one line, then write the body to a file and run
  `gh issue create --repo homebase-id/odin-core --title "…" --label … --body-file <file>`. Give the user the
  URL it prints.

A bug:

```markdown
## Summary
<What goes wrong and for whom, in one or two sentences.>

## Where
- Host: <production NA / EU, bleeding edge, sandbox, local>
- Version: <vX.Y.Z or commit>
- Database: <SQLite / PostgreSQL / both / unknown>
- Caller and API: <owner, app, guest or peer; V1 or V2>

## Steps to reproduce
1. …

## Expected vs actual
- **Expected:** …
- **Actual:** …

## Evidence
<Log lines with their timestamps, a failing test, the request and its response. If nothing is captured
yet, name the test or logging that would capture it.>

## Suspected area (optional)
<file:line leads, framed as leads unless proven.>

## Scope for the plan
<What a fix should and shouldn't touch, and what must not regress.>
```

A feature:

```markdown
## Problem / motivation
<What can't be done today, or what is awkward, and why it matters.>

## Proposed behaviour
<The flow as the caller sees it.>

## Affected areas
<Services, controllers, tables, V1 / V2. Call out a schema change or a new route.>

## Acceptance criteria
- [ ] …

## Scope for the plan
<What's in, and what is explicitly out.>
```

### After filing

- **Correct in place:** when new information arrives, edit the body and title so they stay the one current
  description (`gh issue view <n> --json body -q .body`, edit, `gh issue edit <n> --body-file <file>`), and
  delete correction comments you posted once they are folded in. Comments are for discussion.
- An issue about something a merged PR got wrong starts with "Found after #<PR>" and gets the `post-merge`
  label.
- When a fix doesn't hold, reopen its issue with "Still broken: …" instead of filing a new one.
