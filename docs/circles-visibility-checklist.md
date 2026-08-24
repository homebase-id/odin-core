# Circles & visibility (part 4) — implementation checklist

Line-by-line extraction of every imperative in chat-kmp's `CIRCLES_VISIBILITY_PROPOSAL.md`
(part 4/4), grouped into implementation categories. Line refs are that file at commit
`e3ac1bd`, read 2026-08-19.

*This is a derived working checklist, not a spec. The proposal remains the source of truth; where
this file and that one disagree, that one wins.*

**This is a client-side doc.** Almost all of it is chat-kmp work — it lives here only so the four
checklists sit together. Cat 7 is the part that lands in odin-core, and every item in it is
already specified by `connection-defaults.md`; nothing here adds a server requirement that part 2
doesn't already carry.

## Cat 0 — Blocking decisions

Nothing in Cat 2, 3, or 6 can be built until OQ6 is settled — it fixes every label and icon.

| # | Item | Line |
|---|---|---|
| 0.1 | **Pick the state-name set**: New / Chat / Circle vs New / Known / Trusted, and its emoji triple (👋 💬 ⭕ vs 👋 🤝 🛡️). The doc argues A; the audience case is a concrete strike against B | L794-798 (OQ6) |
| 0.2 | Does the *field visibility* dialog also default to "Any of my circles"? (proposed: yes) | L778-780 (OQ1) |
| 0.3 | Show a visibility pill next to each field in Edit Profile? | L781-782 (OQ2) |
| 0.4 | Do Chat contacts show a subtle 💬 indicator, or nothing? | L787-788 (OQ4) |
| 0.5 | Where does the "All connections" audit view live — contact book or settings/security? | L800-804 (OQ7) |

Resolved and needing no decision: OQ1 main question, OQ3 (inline circle creation: yes, from both
surfaces), OQ5, OQ8, OQ9.

## Cat 1 — Contact state machine

| # | Item | Line |
|---|---|---|
| 1.1 | `isReviewed(): Boolean` — derived from `reviewedAt != null` on connection info; the single source for states and filtering | L493-494 |
| 1.2 | `setReviewed(true)` — performs a chat-only review through the atomic endpoint (stamp, no circles) | L495-498 |
| 1.3 | `setReviewed(false)` — un-review; rejected when the contact holds any personal circle | L499-504 |
| 1.4 | `personalCircles(): List<Circle>` — memberships filtered to `Designation == PERSONAL`, **regardless of owning app** | L507-510 |
| 1.5 | `isInAnyPersonalCircle(): Boolean` | L511-514 |
| 1.6 | Both computed **client-side** from connection info ∩ circle definitions — deliberately no server endpoint | L515-521 |
| 1.7 | Derivation: New = `!reviewed`; Chat = `reviewed && !inAnyPersonalCircle`; Circle = `reviewed && inAnyPersonalCircle` | L511-514, L536-540 |
| 1.8 | Removing the last circle drops to Chat, not New — the stamp persists | L74-76, L547-548 |
| 1.9 | Audience membership never awards ⭕ | L147-152 |

## Cat 2 — Contacts list

| # | Item | Line |
|---|---|---|
| 2.1 | Replace All / Unvetted / Vetted filters with chips: All, New (count badge), plus each actual circle | L240-243 |
| 2.2 | Three states via a **monochrome vector icon** tinted Homebase blue in a fixed trailing slot — not emoji glyphs, which can't be recolored | L244-246, L207-215 |
| 2.3 | New row gets a prominent **Review** action | L247-248 |
| 2.4 | Chat row: second line empty | L249-250 |
| 2.5 | Circle row: second line shows the circles — user emoji as full-color emoji, else a name pill | L251-255 |
| 2.6 | Vowel-dropped pill fallback (Family → `fmly`); full name in roomy contexts and always the a11y label | L254-257 |
| 2.7 | Tapping a contact shows public profile + review CTA | L258 |
| 2.8 | Contact book lists **personal contacts only** — audience members never appear | L259-261 |

## Cat 3 — Review flow

| # | Item | Line |
|---|---|---|
| 3.1 | One modal combining explanation and circle selection | L300-316 |
| 3.2 | Circle list with toggles; special permission circles (Emergency Location Access) visually distinct | L307-308 |
| 3.3 | Per-app default toggles + "Follow their feed" | L309-310 |
| 3.4 | **Adaptive single button**: ⭕ Add to circles with ≥1 selected, 💬 Chat only with none | L312-314, L318-320 |
| 3.5 | Toggles are **visible**, never hidden side effects | L326-328 |
| 3.6 | Suite-aware presentation: own-suite apps collapse to one summary row, a separate app always gets its own row, the arriving app is prominent | L328-332 |
| 3.7 | Deselecting the last circle flips default toggles off but leaves them visible | L335-337 |
| 3.8 | Either tap stamps the review and moves the contact out of New | L340-342 |
| 3.9 | Foreign-app toggles show a **pending** state — checked but not yet active | L344-350 |
| 3.10 | **👋 Keep as new** replaces Cancel — dismisses without stamping | L352-356 |
| 3.11 | Scrim-tap and back gesture keep plain cancel behavior | L355-356 |
| 3.12 | Disconnect / Block stay available as tertiary actions | L361-362 |
| 3.13 | Audience requests never enter this flow | L358-359 |
| 3.14 | Inline circle creation from the review dialog (mints under the profile app) | L783-786 (OQ3) |

## Cat 4 — Profile visibility

| # | Item | Line |
|---|---|---|
| 4.1 | Rename the Edit Profile section "Vetted" → **Visible to my circles**, with subtext | L271-272 |
| 4.2 | Replace the Public / Vetted segmented control with **Public \| Circles** | L276-281 |
| 4.3 | **Select circles** dialog with "☑ Any of my circles" as the prominent default | L283-290 |
| 4.4 | Picker lists **personal** circles by default | L292-293 |
| 4.5 | Per-circle field/photo overrides — the Beer Drinking Buddies photo case | L392-405 |
| 4.6 | Inline circle creation from the visibility picker | L783-786 (OQ3) |
| 4.7 | "Any of my circles" is an **app-maintained enumerated ACL**, fail-closed — never the Reviewed tier | L742-744 |

4.7 is the one that matters for correctness: the Reviewed tier is for low-sensitivity social
surfaces, never the home address.

## Cat 5 — Circles tab

| # | Item | Line |
|---|---|---|
| 5.1 | Collapsible explainer at the top | L377-379 |
| 5.2 | Cards with icon/name, member count + avatar preview, one-line description | L380-383 |
| 5.3 | Optional user-chosen emoji, picker reusing the reaction picker | L384-387 |
| 5.4 | Special circles stay visually distinct | L388 |
| 5.5 | "New connections" as a top item with a count | L389 |
| 5.6 | + Create Circle button | L390 |
| 5.7 | App default circles appear as a visibly-distinct group (member list, owner toggle, read-only grants) — never pills, never states | L131-135, L648-651 |
| 5.8 | Delete hardcoded system-circle GUID knowledge (`circleSortRank()` pinning, the "Unvetted" rename) | L651-653 |

## Cat 6 — Terminology sweep

| # | Item | Line |
|---|---|---|
| 6.1 | Vetted → My circles / Visible to my circles | L223 |
| 6.2 | Unvetted → New | L224 |
| 6.3 | Blue check → circle badge / circle pills, reserved for circle membership | L225 |
| 6.4 | "Confirm" demoted to a verb; buttons name destinations | L226 |
| 6.5 | New terms: Any of my circles, Personal / Audience / Vendor circle, App default circle | L227-231 |
| 6.6 | "Connection" is the constant noun, never a state name | L164-168 |

## Cat 7 — Backend asks (odin-core)

Every item here is already in `connection-defaults-checklist.md`. Listed for traceability.

| # | Item | Line | Part 2 item |
|---|---|---|---|
| 7.1 | `Designation` on the circle record, set by the owning app, default `PERSONAL` | L560-566 | Cat 0 (schema, done) |
| 7.2 | Optional `emoji: String?` on the same record | L587-589 | Cat 0 (schema, done) |
| 7.3 | `reviewedAt` on the connection registration, exposed via GetConnectionInfo | L474-486 | 1.1, 1.4 |
| 7.4 | Viewer-scoped redaction — owner shape vs identity-list | L488-492 | 1.6 |
| 7.5 | Atomic review endpoint | L495-498 | 1.2 |
| 7.6 | Un-review invariant (rejected while PERSONAL memberships exist) | L521-527 | 1.5 |
| 7.7 | Paged server queries for audience members — never materialize as local contact records | L753-760 | — (new; part 2 mentions audience scale only as `ReviewedAt` column rationale) |
| 7.8 | Server-backed "All connections" audit view: every connected identity with the union of its grants, paged | L800-804 | — (new) |

7.7 and 7.8 are the only two server asks in part 4 that part 2 does not already specify. Both are
query surfaces, not schema.

## Cat 8 — Migration and legacy

| # | Item | Line |
|---|---|---|
| 8.1 | Existing contacts with `vetted == true` → treat as reviewed, backfill opportunistically | L543-544 |
| 8.2 | Contacts with circle grants but no stamp → treat as reviewed; membership is evidence | L545-546 |
| 8.3 | Parse the legacy `vetted` field until the V2 shape lands | L490-492 |
| 8.4 | Re-secure today's "Vetted" profile fields as app-maintained personal-circle ACLs | L699-702 |
| 8.5 | Audience approval does **not** stamp and does **not** count toward the New badge | L157-160 |

## Cat 9 — Rendering and Unicode

| # | Item | Line |
|---|---|---|
| 9.1 | Store and render the full emoji string — never substring a ZWJ sequence | L592-593 |
| 9.2 | Desktop JVM emoji fonts lag; the name-pill fallback doubles as the can't-render fallback | L594-595 |
| 9.3 | Vowel-dropping is Latin-specific; other scripts use `truncateToCodePoints` | L596-598 |
| 9.4 | The full circle name is always the `contentDescription` — emoji is never the semantic label | L598-600 |
| 9.5 | State icons and user emoji are different visual species; nothing needs reserving in the picker | L211-215 |

## Two places the doc contradicts itself or part 2

**1. The phasing section is stale.** Section 7, Phase 1 says to *"stamp `connectionReviewedAt` in
the contact's localAppData when the review completes"* (L424-427). Section 8 explicitly revises
this: the stamp is **server-side on the connection registration**, and the localAppData design is
listed as *"Rejected earlier design (kept for the record)"* (L474-486). Section 8 wins. The
phrase `connectionReviewedAt` also survives in prose at L340 and L547 where it should read
`ReviewedAt`.

**2. OQ9 is superseded.** L805-809 says legacy bare-`connected` maps **behavior-identically** to
"member of any `Connect` circle." Part 2 L288-292 explicitly revises this — *"(this revises part
4's earlier mapping)"* — to reviewed-only, a deliberate tightening.

This partly resolves the conflict flagged in `connection-defaults-checklist.md`: part 2's L288 is
the current intent, and part 4's OQ9 is the older mapping it names. But part 2's **own** L120-123
still states the zero-behaviour-change version, so the stale text survives inside part 2 too.
*(Inference from reading the three passages together; the docs do not flag it.)*

## Not in scope here

Part 2 (`connection-defaults.md`) → `connection-defaults-checklist.md`.
Part 3 (`weak-key-retirement.md`) → `weak-key-retirement-checklist.md`.
