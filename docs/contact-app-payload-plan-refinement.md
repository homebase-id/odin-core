# Revised plan: contact large-field overflow + per-app contact data

> **Refinement of [`contact-app-payload-plan.md`](./contact-app-payload-plan.md) (PR #1571).**
> Incorporates owner feedback: drops the per-app payload + key-derivation + client-side-encryption
> approach in favor of one general large-field overflow mechanism shared by core and app data.

## Context

PR #1571 (`docs/contact-app-payload-plan.md`, by todd) proposes letting an app store its own
payload on a contact file. The author's plan gives **each app its own payload**, addressed by a
**server-derived short hash of the appId**, **client-side encrypted**, and read through a **new
dedicated GET endpoint** (because clients can't recompute the derived key).

The owner's feedback rejects that framing. The real problem is narrower and more general:

- A contact's inline JSON (`AppData.Content`) is capped at **10 KB**
  (`AppFileMetaData.MaxAppDataContentLength`). Once a contact accumulates more than ~7 KB of data,
  the overflow must live in a payload.
- This is **not app-specific**. A contact's *own* fields (Bio, memo/description, audit-log) can be
  unbounded too. Large-field overflow should be **one general mechanism shared by core contact data
  and app data alike** — not a bolt-on app feature.
- App data is just *more contact data*, namespaced by the calling appId. Stamping the appId from the
  token is **convenience, not security** — it stops App A from *accidentally* clobbering App B's
  slice. It is not an isolation boundary: an app that funnels through the contact write can already
  nuke a core field (e.g. `name`) today, so this adds no new exposure. The value is avoiding nasty
  future pitfalls by keeping each app confined to its own JSON.

**Intended outcome:** a single, general "small-inline + full-in-payload" mechanism. Core fields and
per-app data both get the *option* of a short/"chopped" preview inline (returned cheaply with the
normal contact query) plus the full value in **one shared, server-encrypted overflow payload**, read
on demand exactly like every other payload (the `../chat-kmp/` generic payload read).

---

## Why several decisions in the old plan are not great

1. **One payload per app → hits a hard ceiling.** `FileMetadata.MaxPayloadsCount = 25`, and
   `prfl_pic` + `merge_log` already consume 2. One-per-app caps a contact at ~23 apps and is exactly
   the "limited number of payloads" problem. → **A single shared overflow payload** (app-namespaced
   *inside*, like `merge_log`) removes the ceiling entirely.

2. **Key derivation (`SHA256(appId)` → 10-char base36) exists only to dodge the payload-key regex**
   (`^[a-z0-9_]{8,10}$`). It drags in: a custom base36 encoder, ~46-bit collision risk needing a
   collision-guard stored in `DescriptorContent`, and a server-internal key the client can't address
   — which is *why* the old plan needs a custom GET. All of it disappears with **one fixed,
   well-known key** (e.g. `ext_data`). No derivation, no collision guard, no ownership bookkeeping.

3. **A new "calling-app resolver" abstraction for reads is unnecessary.** With a fixed key the read
   is the generic payload GET the client already uses for everything else
   (`chat-kmp/.../DriveFileHttpProvider` → `GET /drives/{drive}/files/{file}/payload/{key}` +
   `KeyHeader` decrypt). The server needs the appId only on **writes**, and that's the existing
   3-line lookup `AccessRegistrationId → AppClientRegistration.AppId` (see
   `AppRegistrationService.DeleteCurrentAppClientAsync`) — not a new concept.

4. **Client-side encryption of the payload is inconsistent with the rest of the contact file.** The
   contact JSON and `merge_log` are **server-encrypted** under the file's AES key (per-payload IV).
   The old plan's image-style client-side encryption forces the client to decrypt the file's
   `SharedSecretEncryptedKeyHeader`, derive the per-contact AES key, and encrypt the payload itself —
   extra client crypto for no security gain (the server holds the storage key regardless; the old
   plan admits it isn't zero-knowledge). → **Server-side encryption** (point 3): the client sends
   plaintext JSON over the existing shared-secret transport, identical to `UpdateAsync`.

5. **Framing it as an app-only "slot."** Large-field overflow is general; modeling it as a shared
   core+app mechanism is simpler and also solves the contact's own large fields.

---

## Revised design

### Storage shape

- **Inline** (`AppData.Content`, returned by the normal `QueryBatch` read): `ContactContent`,
  extended with large-capable core fields (e.g. `bio`) holding the **chopped/optional preview**, and
  an `appData` map (`appId → small JSON`, per-app inline budget ~200 B).
- **Overflow** (one new payload `ext_data`, on demand): a `ContactExtendedContent` JSON holding the
  **full** values of any chopped core fields plus `appData` (`appId → full JSON`). Server-encrypted
  under the file's AES key with its own IV, written atomically with the content — **the `merge_log`
  pattern applied to a second payload**. Carried forward unchanged on every other write.

A field (core or app) may be: small-only (inline), or chopped-inline + full-in-`ext_data`. The client
chooses what to chop.

**Sensitive-field guidance (document this for app developers):** the shared `ext_data` payload (and
the inline content) are encrypted under the file's AES key, so they are readable by *any* app with
read access to the contact drive — there is no per-app field isolation (consistent with the
convenience-not-security stance above). If an app deems a particular field genuinely sensitive, it
should either **not store it on the contact record at all**, or **apply its own app-level encryption**
to that value before sending it — so the data is doubly encrypted and opaque to other apps. The server
stores whatever bytes it is given verbatim within the JSON, so app-level encryption is transparent to
this design.

### Reads — nothing new

Inline content arrives with the existing client-side contact `QueryBatch`. The full data is fetched
on demand via the **existing generic payload endpoint** by the fixed key `ext_data`; the response's
`SharedSecretEncryptedKeyHeader64` carries the file AES key + payload IV, and the client decrypts with
`KeyHeader(iv, aesKey)` — the same flow `chat-kmp` already uses for every payload. No new read
endpoint, no resolver, no derivation.

### Writes

- **Core contact write** — extend the existing `PUT /api/v2/contacts/{uniqueId}` (owner,
  `ManageContacts`). `UpdateContactRequest`/`CreateContactRequest` gain an optional
  `ContactExtendedContent Extended`. When present, the server writes/rewrites the `ext_data` payload
  in the **same `UpdateBatchAsync` transaction** as the content (extend `BuildContentManifest` /
  `WriteContentWithMergeLogAsync` to include the `ext_data` descriptor alongside `merge_log`).
  Existing version-tag gating is unchanged.
- **App contact write** — new `PUT /api/v2/contacts/{uniqueId}/app-data` (+ `DELETE` to clear the
  slice). The server resolves the appId from the token and merges **only** `appData[appId]` inline
  (≤ ~200 B; reject over-cap with a clear error) and `ext_data.appData[appId]` (full), leaving core
  fields and other apps' slices untouched. This is a convenience guard against accidental cross-app
  overwrite (see Context) — not a security boundary — so no token-isolation machinery beyond the
  appId stamp is warranted. Use the **`MergeAsync`-style** server-side merge
  (read-modify-write with retry-on-`VersionTagMismatch`), not whole-file client gating: app slices
  are isolated and can't clobber core fields, so this avoids the spurious 409s the old plan
  acknowledged under concurrent enrichment.

### Files to change

- `src/services/Odin.Services/Contacts/ContactContent.cs` — add large-capable core field(s) (`bio`,
  …) + `appData` map; add `ContactExtendedContent` (full values + `appData`).
- `src/services/Odin.Services/Contacts/ContactRequests.cs` — add optional `Extended` to
  create/update requests; add `SetContactAppDataRequest`.
- `src/services/Odin.Services/Contacts/ContactService.cs` — add `ExtendedContentPayloadKey =
  "ext_data"`; a `WriteExtendedPayloadAsync` + `ReadExtendedAsync` mirroring the `merge_log`
  helpers (`WriteContentWithMergeLogAsync` / `ReadMergeLogAsync`); thread `ext_data` through
  `OverwriteAsync` / `BuildContentManifest` / `CarryForwardPayloads`; add `SetAppDataAsync` /
  `DeleteAppDataAsync` (namespaced merge resolving appId).
- `src/apps/Odin.Hosting/UnifiedV2/Connections/V2ContactsController.cs` — extend the PUT body; add
  `PUT`/`DELETE .../app-data`; inject `AppRegistrationService` and resolve the appId via the existing
  `AccessRegistrationId → AppClientRegistration.AppId` lookup (a thin helper, not a new abstraction).

### Reuse (do not reinvent)

`merge_log` write/read (`ContactService.WriteContentWithMergeLogAsync`, `ReadMergeLogAsync`),
`BuildContentManifest`, `CarryForwardPayloads`, `UpdateBatchAsync` + `PayloadInstruction` /
`PayloadUpdateOperationType`, `MergeAsync` retry pattern, `KeyHeader.EncryptDataAes`/`Decrypt`.

---

## Open decisions (recommendations; finalize in implementation)

- **Overflow doc shape** — `ContactExtendedContent { bio…, appData }` as above (recommended), vs. a
  generic `fieldPath → fullValue` map. Recommend the typed shape for core fields + an `appData` map
  for free-form app data.
- **App-data concurrency** — server-side namespaced merge with retry (recommended) vs. whole-file
  version-tag gate. Recommend the merge to avoid spurious conflicts.
- **Per-app inline cap** — default ~200 B, configurable; over-cap write rejected with guidance to use
  `ext_data`.

---

## Verification

- **Unit** (`Odin.Services.Tests`): `ext_data` round-trips full values; chopping a core field keeps
  the preview inline and the full value in `ext_data`; per-app inline cap is enforced.
- **Integration** (`Odin.Hosting.Tests`, app-token client; mirror the existing V2 contacts setup):
  - Core write with `Extended` → inline content + `ext_data` payload both present and decryptable.
  - App writes its `appData[appId]`; a **different app** never sees/overwrites it; core fields and
    `merge_log` / `prfl_pic` survive (carry-forward).
  - `ext_data` read via the **generic** payload endpoint (no contact-specific endpoint) decrypts with
    the file key header.
  - `ext_data` is absent from a default contact `QueryBatch` (on-demand only).
  - App-data delete clears only that app's slice.
- **Build/regression:** `dotnet build ./odin-core.sln`; `dotnet test` the contacts + V2 contacts
  suites on SQLite (and PostgreSQL via `docker/start-dev-servers.sh`).
</content>
