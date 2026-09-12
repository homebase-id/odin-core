# Finalizing `connection-defaults.md`

What remains before `docs/connection-defaults.md` is a finished document rather than a working
one. Excludes the two items already known and tracked elsewhere: dropping the old system circles,
and replacing the temporary weak key.

Each item below is the outstanding task, then what makes it a problem, then the ways out.

---

## 1. Resolve the remainder of open question 1

**Task.** Decide whether enabling an app later offers enrollment of *existing* connections (prompt
once, default off) or applies to future connections only.

**Problem.** Installing an app mid-life is the common case, not the edge one, and an owner with two
hundred existing contacts gets an app that works for nobody until each contact is re-reviewed. The
doc resolves the consent question but leaves this dangling, and it is the half that decides whether
app installation feels finished or broken.

**Candidates.**
- *Future-only.* Simplest, no new UX, no bulk grant; the owner discovers the gap contact by contact.
- *One-time prompt, default off.* Matches the doc's own leaning; needs a bulk-enrollment path and a
  progress story for large contact lists.
- *Offer it from the app's own page, any time.* Decouples the decision from install, so an owner who
  declines at install can change their mind without reinstalling — more surface, but no forced choice
  at the least-informed moment.

---

## 2. Resolve open question 2

**Task.** Decide whether `Review` circles carry permission keys (`AllowIntroductions`,
`ReadWhoIFollow`) or those become per-connection settings toggled at review.

**Problem.** Permission keys are identity-wide while circles are drive-scoped, so a key on a circle
grants something the circle's own model cannot describe. The doc says either answer avoids new
schema, which means nothing forces the decision and it can stay open indefinitely.

**Candidates.**
- *Keys on verified circles only.* Least movement; the deposit-only invariant already forbids them on
  auto circles, so the rule is "keys require review" stated once.
- *Keys leave circles entirely, become per-connection settings.* Cleaner conceptually — an
  identity-wide grant lives on the identity relationship, not on a drive-scoped grouping — but every
  existing circle carrying keys needs migrating.
- *Defer explicitly.* Record it as deliberately unresolved with a trigger condition, so it stops
  reading as an oversight.

---

## 3. Fix the broken cross-reference

**Task.** Line 257 refers to "open question 5"; the Open questions section contains only two entries.

**Problem.** A reader following the pointer finds nothing, and cannot tell whether the question was
answered, renumbered, or dropped. It also suggests the section was longer once, so the reader
wonders what else went missing.

**Candidates.**
- *Renumber the reference to 1*, which is where that content now lives.
- *Restore the missing questions* if they were cut rather than merged.
- *Replace numeric references with anchor links* so the next renumber cannot break them.

---

## 4. Record what has already shipped

**Task.** Add `> **Built:**` markers for the parts now in the tree. Only one exists (viewer-scoped
redaction), yet `ReviewedAt`, the atomic `review` / `review/clear` endpoints, circle and drive
`AppId`, pending enrollments and the `ProcessEnrollments` socket command are all implemented.

**Problem.** The document reads as entirely future work, so a reader cannot tell proposal from
shipped behavior, and anyone estimating the remaining work overestimates it substantially.

**Candidates.**
- *Per-section `Built:` blockquotes*, matching the one that already exists — consistent, no
  restructuring.
- *A status table at the top* mapping each section to proposed / built / partial, cheaper to keep
  current than prose scattered through the document.
- *Split the document* into "shipped" and "proposed" halves; clearest for readers, most disruptive to
  existing links.

---

## 5. Give the security-ladder recut a status

**Task.** State plainly that the recut is unimplemented. `SecurityGroupType` still has
`AutoConnected = 555` and `Connected = 777`, and no `Reviewed` member exists.

**Problem.** The section is written in the present tense with a table headed "The end state", which
reads as a description of current behavior; a reader could reasonably write an ACL against `Reviewed`
today and find it does not exist. It is the largest section in the document and the most likely to
be mistaken for shipped.

**Candidates.**
- *A one-line "Proposed — not yet implemented" banner* under the heading; smallest possible change.
- *Rewrite the section in future tense* throughout, which removes the ambiguity but touches a lot of
  prose.
- *Move it to its own document* referenced from here, so its status is unambiguous by separation.

---

## 6. Decide the standing of `Designation` and `Emoji`

**Task.** The doc frames these as fields chat-kmp PR #1062 "wants"; both now exist on
`CircleDefinition` as `CircleDesignation` and `Emoji`.

**Problem.** A shipped field described as a request from another team's PR makes the document wrong
about its own subject, and leaves unclear whether the server's implementation matches what that PR
asked for.

**Candidates.**
- *Restate as built*, with a line on what the server implemented versus what was requested.
- *Verify against PR #1062 first*, then restate — the honest option if nobody has checked that the
  shipped shape matches.
- *Drop the reference entirely* and describe the fields on their own terms, since the provenance
  stops mattering once they exist.

---

## 7. Write the release-note text for the ACL tightening

**Task.** The migration section flags the bare-`connected` → reviewed-only change as a behavior
change to "call out in release notes"; no wording exists.

**Problem.** The change silently removes access from unreviewed connections to anything with a
bare-`connected` ACL — including today's "Vetted" profile fields — and an owner who does not read
about it first will experience it as data loss. Writing it at release time, under pressure, is how
it gets one line and no migration guidance.

**Candidates.**
- *Draft it now into this document*, so the wording is reviewed alongside the design that motivates it.
- *Write an owner-facing migration note*: what changes, who loses access, and how to restore it by
  reviewing contacts.
- *Add a pre-migration report* the owner can run to see which contacts would lose what — more work,
  but converts a surprise into a decision.

---

## 8. Add the drive-side adoption path

**Task.** The document covers a circle gaining an `AppId`; the equivalent for orphaned drives now
ships and is undocumented here.

**Problem.** Drives are the larger half — every drive predating the addressing work carries a null
`AppId` — and adoption also assigns the slug that makes a drive addressable, which has no circle
analogue. A reader of this document would not know the drive path exists.

**Candidates.**
- *A short subsection beside the circle path*, noting the slug invariant (`AppId` and `DriveSlug` set
  together or both null) and that adoption is one-way.
- *Cross-reference `drive-addressing.md`* instead, keeping drive mechanics in one place and this
  document about connections.
- *Cover both under one "adoption" heading*, since the rule and the escape hatch are now the same
  shape for circles and drives.
