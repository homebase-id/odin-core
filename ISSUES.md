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

- An issue about something a merged PR got wrong starts with "Found after #<PR>" and gets the `post-merge`
  label.
- When a fix doesn't hold, reopen its issue with "Still broken: …" instead of filing a new one.
