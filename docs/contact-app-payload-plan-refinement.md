# Plan: per-app contact data — the list-vs-detail rule

> **Refinement of [`contact-app-payload-plan.md`](./contact-app-payload-plan.md) (PR #1571), and an
> alternative to [`contact-app-data-separate-file-plan.md`](./contact-app-data-separate-file-plan.md).**
> One HomebaseFile per contact. Size-capped fields (core + a small per-app blob) live in the contact
> JSON; bulk, on-demand data lives in a payload. No per-app payloads, no key derivation, no
> calling-app resolver, no client-side encryption, no preview/chop split.

## The governing principle

> **List/display reads never touch payloads; opening a single contact may.**

Apps load **all** contacts into memory at startup and render/filter the list from that. A spinner to
pull a 20 KB bio for *one* contact the user explicitly opened is fine; N spinners to draw the list is
not. This is the litmus test for every "where does this field go?" decision:

- **Needed to render/filter the list** → a **size-capped field in the contact JSON** (`AppData.Content`).
- **Only needed when a single contact is opened** → a **payload** (one network load, on demand).

Everything below follows from this one rule.

## Why the JSON is the only home for list data

- Contact content **rides inline in the file header and never spills to a payload** (verified in
  `chat-kmp .../contacts/Contact.kt`). The list `QueryBatch` returns that content and nothing else —
  so anything *not* in the contact JSON is invisible to the list.
- To show data stored elsewhere (a payload, or a separate file), the app must fetch it **per contact
  record** — a network load each on cold start (unless cached). That is exactly the fan-out the
  principle forbids.
- Because content is always inline and never overflows gracefully, **every JSON field must be
  individually size-capped.** A 7 KB display name would bloat every contact in the list query. Caps
  keep the whole list loadable in one cheap batch.

---

## Why not the prior approaches

### The original per-app-payload plan (PR #1571)

1. **One payload per app → a hard ceiling.** `FileMetadata.MaxPayloadsCount = 25`, and `prfl_pic` +
   `merge_log` already consume 2. One-per-app caps a contact at ~23 apps.
2. **Key derivation (`SHA256(appId)` → 10-char base36)** exists only to dodge the payload-key regex
   (`^[a-z0-9_]{8,10}$`). It drags in a custom base36 encoder, ~46-bit collision risk needing a
   collision-guard in `DescriptorContent`, and a server-internal key the client can't address — which
   is *why* it then needs a custom GET.
3. **A "calling-app resolver" abstraction for reads is unnecessary.** The server needs the appId only
   on **writes**, via the existing `AccessRegistrationId → AppClientRegistration.AppId` lookup (see
   `AppRegistrationService.DeleteCurrentAppClientAsync`).
4. **Client-side payload encryption is inconsistent** with the contact JSON and `merge_log`, which are
   **server-encrypted** under the file AES key. It forces extra client crypto for no security gain
   (the server holds the storage key regardless; not zero-knowledge either way).

### The separate-file plan ([`…-separate-file-plan.md`](./contact-app-data-separate-file-plan.md))

Separate files (`FileType=389`, one per app+contact) are a reasonable instinct, but they solve
problems we don't have and not the one we do:

- **They don't solve what's blocking us.** App data in a `389` file isn't in the contacts list query,
  so to render it the app fetches **per contact record** on cold start — the forbidden fan-out, just
  relocated.
- **They lose atomicity.** Contact and app-data become separate files that sync independently →
  partial sync, "didn't get all the data," orphaned `389` files whose contact is gone. ("Too risky if
  a file goes missing.")
- **Cleanup becomes best-effort** — sweep-queries by `groupId`/`appId` tag plus a periodic reconcile
  backstop for orphans. A single file deletes its data for free.
- **More moving IDs** — `md5(appId+odinId)`, `md5(odinId)`, an `appId` tag to keep consistent.
- **It doesn't even remove the overflow it targets** — each `389` file still hits the same 10 KB cap;
  a 20 KB blob still needs payloads on its own file.
- **The "write contention" it relieves isn't real** — one app on one phone plus occasional
  server-side enrichment, all handled by the version-tag + merge-retry already in the code
  (`MergeAsync`). The only genuine case (same app, two devices) needs a version tag under *either*
  design.

| Concern (under "load all contacts at startup") | Separate file (`389`) | Single HomebaseFile + payload |
|---|---|---|
| List/display fields available in memory | ✗ fetched per contact record (a load each, cold) | ✅ already in the contacts query |
| Atomic contact + app data | ✗ independent files, can desync | ✅ one file, one version tag |
| "File went missing" / orphans | ✗ N sidecar files, each can orphan | ✅ nothing to lose separately |
| Cleanup on contact/app delete | ✗ sweep queries + reconcile backstop | ✅ delete the file, done |
| Derived IDs to manage | ✗ three keys | ✅ contact id + `appId` from token |
| 10 KB overflow | ✗ still hit per file | bulk goes to a payload (on demand) |
| Unblocks the emergency feature now | ✗ | ✅ one JSON field |

---

## The model

### Two buckets

- **Contact JSON** (`AppData.Content`, in every list query): core contact fields, **each individually
  size-capped** (a display name is ≤ a couple hundred chars — no 7 KB names), plus a per-app
  **opaque blob capped at ~N bytes** (~200). The server stores the blob **verbatim** and never parses
  it; it is keyed by the `appId` resolved from the token. This is the only tier the emergency/location
  feature needs.
- **Payload** (on demand, per contact): anything bigger — a 20 KB bio, an app's bulk data. Read only
  when a single contact is opened, via the existing generic payload endpoint. A field is either small
  enough for its JSON cap **or** it lives wholesale in a payload — never split across both.

> The exact **bulk-payload shape** is the one genuinely open question and does **not** block the
> emergency feature. Recommended: a single shared, server-encrypted `ext_data` payload, app-namespaced
> inside (the `merge_log` pattern), to avoid the per-app `MaxPayloadsCount=25` ceiling. Settle this
> increment separately.

### appId stamp = convenience, not security

Stamping the appId from the token stops App A from *accidentally* clobbering App B's blob. It is **not**
an isolation boundary: an app that funnels through the contact write can already nuke a core field
(e.g. `name`), so this adds no new exposure. The value is keeping each app confined to its own JSON to
avoid future pitfalls — no token-isolation machinery beyond the stamp is warranted.

### Sensitive-field guidance (document for app developers)

The contact JSON and any payload are encrypted under the file's AES key, so they are readable by *any*
app with read access to the contact drive — there is no per-app field isolation (and it is not
zero-knowledge: the owner's server holds the storage key). If an app deems a value genuinely
sensitive, it should either **not store it on the contact record**, or **apply its own app-level
encryption** before sending it (doubly encrypted, opaque to other apps). The server stores the bytes
verbatim, so app-level encryption is transparent to this design.

### Encryption — server-side

The client sends **plaintext** JSON over the existing shared-secret transport; the server encrypts at
rest under the file AES key — identical to `ContactService.EncryptContent` and the `merge_log`
payload. No client-side crypto, no key derivation.

### Reads — nothing new

The list is the existing client-side contact `QueryBatch` (core fields + per-app blobs ride inline).
Bulk data is the existing generic payload GET, on demand, decrypted with the file key header
(`KeyHeader(iv, aesKey)`) — the same flow `chat-kmp` already uses for every payload.

### Writes

- **Core contact write** — the existing `PUT /api/v2/contacts/{uniqueId}` (owner, `ManageContacts`),
  version-tag gated as today. Enforce per-field size caps.
- **App blob write** — `PUT /api/v2/contacts/{uniqueId}/app-data` (+ `DELETE`). The server resolves
  the appId from the token and merges **only** `appData[appId]` (≤ ~N bytes; reject over-cap with a
  clear error), leaving core fields and other apps' blobs untouched. Use the **`MergeAsync`-style**
  server-side namespaced merge (read-modify-write, retry on `VersionTagMismatch`) so an app write
  never spuriously conflicts with enrichment or core edits.

---

## Files to change

- `src/services/Odin.Services/Contacts/ContactContent.cs` — add an `appData` map
  (`appId → opaque JSON string`, per-entry size-capped); enforce per-field size caps on core fields.
- `src/services/Odin.Services/Contacts/ContactRequests.cs` — `SetContactAppDataRequest { string Content; Guid VersionTag }`;
  reuse `ContactWriteResponse` / `ContactWriteConflict`.
- `src/services/Odin.Services/Contacts/ContactService.cs` — `SetAppDataAsync` / `DeleteAppDataAsync`
  (namespaced merge resolving appId, `MergeAsync` retry pattern); per-field + per-blob size validation.
- `src/apps/Odin.Hosting/UnifiedV2/Connections/V2ContactsController.cs` — add `PUT`/`DELETE .../app-data`;
  inject `AppRegistrationService` and resolve the appId via the existing
  `AccessRegistrationId → AppClientRegistration.AppId` lookup (a thin helper, not a new abstraction).
- *(Bulk-payload tier, separate increment)* — `ext_data` write/read mirroring the `merge_log` helpers
  (`WriteContentWithMergeLogAsync` / `ReadMergeLogAsync`), threaded through `BuildContentManifest` /
  `CarryForwardPayloads`.

## Reuse (do not reinvent)

`ContactService.GetWriteContext`, `GetForWritingAsync`, `EncryptContent`/`DecryptContent`, the
`MergeAsync` retry pattern, the `merge_log` payload mechanics (`WriteContentWithMergeLogAsync` /
`ReadMergeLogAsync`, `BuildContentManifest`, `CarryForwardPayloads`, `UpdateBatchAsync` +
`PayloadInstruction`), the `AppRegistrationService` access-reg → appId lookup.

---

## Open decisions

- **Per-app blob cap / per-field caps** — default ~200 B per app blob; sensible per-field caps on core
  fields (e.g. display name ≤ 256 chars). Configurable; over-cap writes rejected with guidance to use
  a payload.
- **Bulk-payload shape** — recommended single shared `ext_data` (app-namespaced, server-encrypted) to
  dodge the 25-payload ceiling. Separate increment; does not block the JSON tier.

---

## Verification

- **Unit** (`Odin.Services.Tests`): per-field and per-blob size caps are enforced; an app blob
  round-trips verbatim; the appId stamp routes a write to the right `appData[appId]` slot.
- **Integration** (`Odin.Hosting.Tests`, app-token client; mirror the existing V2 contacts setup):
  - App writes its `appData[appId]`; it comes back in the default contact `QueryBatch` (list-visible,
    no payload load); a **different app** never sees/overwrites it; core fields, `merge_log`,
    `prfl_pic` survive.
  - An over-cap app blob (or core field) is rejected with a clear error.
  - App-data delete clears only that app's blob.
  - *(Bulk tier)* a 20 KB value is absent from the list `QueryBatch` and fetched only via the generic
    payload endpoint.
- **Build/regression:** `dotnet build ./odin-core.sln`; `dotnet test` the contacts + V2 contacts
  suites on SQLite (and PostgreSQL via `docker/start-dev-servers.sh`).
