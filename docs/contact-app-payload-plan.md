# Implementation plan: per-app contact payload

## Locked behavior
- One payload slot per **app** per contact, keyed by a server-derived short key from the app id; the key is internal and never seen by clients.
- App addresses its slot by `(contactUniqueId, authenticated appId)`; appId comes from the token, so apps are isolated and can't spoof each other.
- Writes/deletes are **whole-file version-tag gated** (same model as `UpdateAsync`/`SetImageAsync`): a stale tag → `409` with the current contact; the app must re-read the whole contact and retry. This enforces "the app always holds the latest contact."
- Payload is **server-side encrypted** (the `merge_log` pattern), so the app sends/receives plaintext bytes and never deals with keys/IVs.
- Read is explicit-demand only (excluded from default contact `QueryBatch`).

---

## 1. Key derivation — new `ContactAppPayloadKey` (static, Contacts namespace)

```csharp
public static class ContactAppPayloadKey
{
    public const string Prefix = "a";   // namespaces app slots; cannot collide with prfl_pic / merge_log

    // "a" + 9 base36 chars (0-9a-z) = 10 chars → matches ^[a-z0-9_]{8,10}$. ~46 bits.
    public static string Derive(Guid appId)
    {
        var hash = SHA256(appId.ToByteArray());          // stable
        var n = BitConverter.ToUInt64(hash, 0) % Pow(36, 9);
        return Prefix + Base36(n).PadLeft(9, '0');
    }
}
```
- Put it in its own static class so it's unit-testable without a `ContactService`.
- Add a ~10-line lowercase base36 encoder (or reuse one if present in `Odin.Core`).

## 2. Calling-app resolver — `AppRegistrationService.GetCallingAppIdAsync`

No accessor exists today, so add one (mirrors `DeleteCurrentAppClientAsync`):
```csharp
public async Task<Guid> GetCallingAppIdAsync(IOdinContext odinContext)
{
    var arid = odinContext.Caller.OdinClientContext?.AccessRegistrationId;
    if (arid == null) throw new OdinSecurityException("App context required"); // owner/guest tokens rejected
    var client = await clientRegistrationStorage.GetAsync<AppClientRegistration>(arid)
        ?? throw new OdinClientException("Invalid access reg id", OdinClientErrorCode.InvalidAccessRegistrationId);
    return client.AppId;
}
```
Resolves from the **token** (`AccessRegistrationId → AppClientRegistration.AppId`); never from request body. This is the isolation guarantee.

## 3. `ContactService` — three public methods + private helpers

`ContactService` currently injects only `ILogger` + `StandardFileSystem`; it keeps that — the appId is resolved in the controller and passed in (keeps the service free of app-registration concerns and makes it unit-testable).

```csharp
// All gated on the whole-file version tag; return ContactWriteResult (reuses existing outcomes).
Task<ContactWriteResult> SetAppPayloadAsync(Guid uniqueId, Guid appId, byte[] content, string contentType, Guid versionTag, IOdinContext ctx);
Task<ContactAppPayloadReadResult> GetAppPayloadAsync(Guid uniqueId, Guid appId, IOdinContext ctx);
Task<ContactWriteResult> DeleteAppPayloadAsync(Guid uniqueId, Guid appId, Guid versionTag, IOdinContext ctx);
```

**`SetAppPayloadAsync`** (mirror `WriteImagePayloadAsync`, minus thumbnails):
1. `writeContext = GetWriteContext(ctx)` (existing bypass-ACL-write upgrade).
2. `existing = GetForWritingAsync(uniqueId, writeContext)`; null → `NotFound` (can't attach to a missing contact).
3. `existing.VersionTag != versionTag` → `VersionConflictResult(...)` (returns current tag + contact).
4. `appKey = ContactAppPayloadKey.Derive(appId)`.
5. **Collision guard:** if `existing` already has a payload with `appKey` whose `DescriptorContent != appId` → throw/log (astronomically rare; detectable because we record the owner).
6. Encrypt `content` server-side under the file AES key + fresh IV; stage via `WriteUploadStream`.
7. Build descriptor: `{ Key=appKey, Uid, Iv, ContentType=contentType, BytesWritten, DescriptorContent = appId.ToString(), LastModified }`.
8. `manifest = BuildContentManifest(existing, freshContentKeyHeader, contentBase64, content, [..CarryForwardPayloads(existing, appKey), descriptor], [AppendOrOverwrite appKey])` → `UpdateBatchAsync` → cleanup → return `Updated` + new version tag.

This reuses `BuildContentManifest` / `CarryForwardPayloads` verbatim, so content + `merge_log` + `prfl_pic` + **other apps' slots** are all preserved, and the content IV rotates as required.

**`GetAppPayloadAsync`** (mirror `ReadMergeLogAsync`):
- Load header; `appKey = Derive(appId)`; find descriptor; absent → not-found.
- `GetPayloadStreamAsync(file, appKey, …)`, decrypt with `{ Iv=descriptor.Iv, AesKey }`.
- Return `{ Content (plaintext), ContentType = descriptor.ContentType, VersionTag = header.VersionTag }`.

**`DeleteAppPayloadAsync`** (mirror `RemoveImagePayloadAsync`): version-gate → manifest with `DeletePayload` instruction → return new tag.

## 4. DTOs — `ContactRequests.cs`
```csharp
public class SetContactAppPayloadRequest { public byte[] Content; public string ContentType; public Guid VersionTag; }
public class ContactAppPayloadReadResult { public byte[] Content; public string ContentType; public Guid VersionTag; } // service-internal
public class ContactAppPayloadResponse { public string Content /*base64*/; public string ContentType; public Guid VersionTag; }
```
Reuse existing `ContactWriteResponse` / `ContactWriteConflict` for write/delete responses. (No new outcome values needed — `Updated`/`NotFound`/`VersionConflict` cover it.)

## 5. `V2ContactsController` — three endpoints
Already `[UnifiedV2Authorize(UnifiedPolicies.OwnerOrApp)]` on `/api/v2/contacts`. Add:
```csharp
[HttpPut("{uniqueId:guid}/app-payload")]    SetAppPayload(uniqueId, [FromBody] SetContactAppPayloadRequest req)
[HttpGet("{uniqueId:guid}/app-payload")]    GetAppPayload(uniqueId)
[HttpDelete("{uniqueId:guid}/app-payload")] DeleteAppPayload(uniqueId, [FromQuery] Guid versionTag)
```
Each: resolve `appId = await appRegistrationService.GetCallingAppIdAsync(WebOdinContext)` (rejects owner/guest tokens → enforces app-only), call the service, then reuse `MapWrite(result)` for PUT/DELETE; GET returns `ContactAppPayloadResponse` (or `NotFound()`). The dedicated GET is **required** because clients can't use the generic drive-payload endpoint — they don't know the key.

## Encryption, authorization, concurrency (consolidated)
- **Encryption:** server-side, file AES key + per-payload IV (identical to `merge_log`). Server sees plaintext app data — consistent with the trust model (the server already handles contact content plaintext; ContactDrive is `OwnerOnly`).
- **Authorization:** caller must present an **App** token (resolver rejects others) and have ContactDrive access (the existing bypass-write upgrade already presumes a read grant; `GetDriveStorageKey` fails otherwise).
- **Concurrency:** whole-file version-tag gate; background enrichment bumping the version will surface as `409` and the app re-reads + retries — intended.

---

## 6. Tests

**Unit** (`Odin.Services.Tests` or `Odin.Core.Tests`): `ContactAppPayloadKey.Derive` is deterministic, matches `^[a-z0-9_]{8,10}$`, starts with `a`, and two distinct app ids → distinct keys.

**Integration** (`Odin.Hosting.Tests`, app-token client — mirror the existing V2 contacts test setup): new `ContactAppPayloadTests`:
- Write then read back round-trips (plaintext + contentType).
- Stale `versionTag` → `409`/`VersionConflict` carrying current contact.
- Successful write advances the contact version tag.
- **Carry-forward:** app payload survives a subsequent contact content update / image write / enrichment merge.
- **Isolation:** app B's GET never returns app A's payload (distinct derived keys).
- Delete removes the slot (and is version-gated).
- App payload is **not** present in a default contact `QueryBatch` (explicit-demand).

---

## Out of scope / follow-ups
- Orphan cleanup when an app is uninstalled (purge its slots across contacts) — needs a background reconcile; flag for later.
- Owner-only enumeration of all app slots (read `DescriptorContent` to list owners) — easy add if needed.
- Multiple named slots per app — would change the key to `Derive(appId, subName)`; not in this version.

## Two micro-decisions (defaults unless overridden)
1. **GET response shape** — JSON `{ content: base64, contentType, versionTag }` (simple, fine for small app metadata) vs. a streamed `FileContentResult` + version-tag header (better for large blobs). **Default: JSON.**
2. **Permission floor** — require only App-token + ContactDrive access (app-owned slot), vs. also asserting `ManageContacts` for parity with the other contact writes. **Default: do not require `ManageContacts`.**
