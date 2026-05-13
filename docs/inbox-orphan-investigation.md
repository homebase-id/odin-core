# Inbox Temp File Orphans — Investigation

## TL;DR

While reviewing yagni.dk's tenant directory we found two sets of leftover files in the per-drive peer-inbox staging dir:

```
1eeddc19b09c3800bc865185fc4ac56b.convo_img-116415638693478400.payload
1eeddc19b09c3800bc865185fc4ac56b.convo_img-116415638693478400-{1080x810,1588x1191,320x240,640x480}.thumb
a8ecdc19500e8d003a0b895e973c3f24.convo_img-116415638693478400.payload
a8ecdc19500e8d003a0b895e973c3f24.convo_img-116415638693478400-{1080x810,1588x1191,320x240,640x480}.thumb
```

This is **not an edge case**. Cross-checking the day's log shows the same pattern leaking on at least four tenants (yagni.dk, toddmitchell.me, bishwajeetparhi.dev, shelly.silberberg.dk) — ten orphan groups in total — every time a peer file update is sent over the chat/conversation drive. We only saw two because we only looked at one tenant's data dir.

There are two problems chained together: a **sender-side bug** that ships payload bytes the recipient doesn't need, and a **recipient-side cleanup bug** that fails to remove what was staged. The recipient bug is already fixed; the sender bug remains.

---

## Investigation

### 1. The leaked files come from `PeerFileUpdateWriter` updates

Both yagni.dk inbox FileIds (`a8ecdc19…`, `1eeddc19…`) appear in the log only as inbox items processed by `PeerFileUpdateWriter` — peer file *updates* over the conversation drive `9ff813af-f2d6-1e2f-9b9d-b189e72d1a11`, sent from `michael.seifert.page` for GTID `684bd03d-…`. Both items succeeded:

```
PeerFileUpdateWriter UpdateExistingFile - file: FileId=… Payload count: 1. GTID: "684bd03d-…"
…
PeerFileUpdateWriter UpdateExistingFile - success: true committed payload count 0
Item with file (…) Processed.  success: "HasBeenMarkedComplete"
CleanupInboxFiles called - file: FileId=… Drive=9ff813af-…
CleanupInboxFiles Deleting additional File: …/inbox/{fileId}.metadata
CleanupInboxFiles Deleting additional File: …/inbox/{fileId}.transferkeyheader
```

Cleanup deleted `.metadata` and `.transferkeyheader`. It did **not** delete the `.payload` or `.thumb` files, which are what we found orphaned.

The mismatch in the log is the key:

> `Payload count: 1` (incoming metadata had a payload descriptor) **but** `committed payload count 0` (`UpdateBatchAsync` committed nothing).

### 2. Recipient-side cleanup bug (already fixed)

`InboxStorageManager.CleanupInboxFilesInternal` was given the *committed* payloads list to drive cleanup. When commit returned 0, the function early-returned and skipped payload/thumbnail deletion, while the outer `CleanupInboxFiles` still removed `.metadata` + `.transferkeyheader`. Net result: payload + thumbs orphaned in inbox temp.

Additionally the original guard

```csharp
if (!descriptors?.Any() ?? false) return;   // wrong; NREs on null, inverted intent
```

was buggy in its own right — it skipped on empty (correct) but would NRE on null.

**Fix applied**

- `InboxStorageManager.cs:75` — guard rewritten to `if (descriptors == null || descriptors.Count == 0) return;`
- `PeerFileUpdateWriter.cs` — both `WriteNewFile` and `UpdateExistingFile` paths now return `incomingMetadata.Payloads ?? []` (the descriptor list from the staged metadata file) instead of the committed list. Cleanup now deletes every payload/thumb that was staged, not only what got committed.

This change alone closes the leak. But it only treats the symptom.

### 3. The pattern is consistent, not rare

For one log day on this server:

| Pattern | Count |
|---|---|
| `Payload count: 0` updates | 22 |
| `Payload count: 1` AND `committed payload count 1` | 2 |
| `Payload count: 1` AND `committed payload count 0` | **10** |

The 10 "shipped but not committed" cases all targeted drive `9ff813af-…` (the chat drive) and span four recipient tenants:

| Tenant | Inbox FileId | Time |
|---|---|---|
| yagni.dk | `a8ecdc19-500e…` | 11:54:31 |
| toddmitchell.me | `a8ecdc19-a00d…` | 11:54:31 |
| bishwajeetparhi.dev | `a8ecdc19-100e…` | 11:57:39 |
| bishwajeetparhi.dev | `1eeddc19-909f…` | 12:03:48 |
| **yagni.dk** | `1eeddc19-b09c…` | 12:07:25 |
| toddmitchell.me | `1eeddc19-7099…` | 12:07:26 |
| shelly.silberberg.dk | `32eddc19…` | 13:24:20 |
| shelly.silberberg.dk | `45eddc19…` | 13:24:20 |
| shelly.silberberg.dk | `48eddc19…` | 13:24:20 |
| shelly.silberberg.dk | `53eddc19…` | 13:24:21 |

The 2 "good" `committed = 1` updates were on a different drive (`283a9d14-…`) and were genuine payload updates. The drive matters because the chat drive is where header-only updates with payload descriptors happen routinely.

---

## Root cause: sender ships every payload regardless of op type

`OutboxWorkerBase.PackageFileStreamsAsync` (`src/services/Odin.Services/Peer/Outgoing/Drive/Transfer/Outbox/OutboxWorkerBase.cs:167-205`):

```csharp
var shouldSendPayloads = !redactedMetadata.PayloadsAreRemote;
if (shouldSendPayloads)
{
    foreach (var descriptor in redactedMetadata.Payloads ?? new List<PayloadDescriptor>())
    {
        var p = await fileSystem.Storage.GetPayloadStreamAsync(file, payloadKey, null, odinContext);
        payloadStreams.Add(p.Stream);
        // …
        foreach (var thumb in descriptor.Thumbnails ?? new List<ThumbnailDescriptor>())
        {
            // reads & ships every thumbnail
        }
    }
}
```

This method is shared by `SendFileOutboxWorkerAsync` (new-file sends — correct, ship everything) **and** `UpdateRemoteFileOutboxWorker` (file updates — wrong). For updates it walks `metadata.Payloads` and reads + ships every payload + thumbnail to the recipient **without consulting the manifest's `PayloadUpdateOperationType`**.

Meanwhile on the recipient, `DriveStorageServiceBase.UpdateBatchAsync` only commits payloads whose op is `AppendOrOverwrite` or `DeletePayload` (per `BatchUpdateManifest.cs:36-41`):

```csharp
public enum PayloadUpdateOperationType
{
    None = 0,                  // skipped by UpdateBatchAsync
    AppendOrOverwrite = 2,
    DeletePayload = 3
}
```

So the contract mismatch is:

| Stage | Behaviour |
|---|---|
| Sender outbox (`PackageFileStreamsAsync`) | Ships bytes for **every** descriptor, regardless of op type. |
| Recipient commit (`UpdateBatchAsync`) | Commits only descriptors whose op is `AppendOrOverwrite` / `DeletePayload`. |
| Recipient cleanup (was) | Walked the *committed* list, missing never-committed-but-staged files → orphan. |

Why the chat drive triggers it consistently: header-only updates on a chat message-with-attachment (reactions, read receipts, edits to text) re-send the existing `metadata.Payloads` descriptor list (so the recipient knows what payloads the file has) but with `PayloadUpdateOperationType = None` for those descriptors in the manifest. Every such update ships the image + 4 thumbnails over the wire, then orphans them in inbox temp.

---

## Proposed solution: skip payloads at the sender

Two options, in order of cleanliness:

### Option 1 — Make `PackageFileStreamsAsync` op-type aware

Change the signature so the update path passes the `BatchUpdateManifest.PayloadInstruction` list. Only include payload + thumbnail streams for descriptors whose `OperationType == AppendOrOverwrite`. `None` ships nothing; `DeletePayload` ships nothing (it only needs the key, not the bytes).

Cleanest, also documents the contract.

### Option 2 — Filter in `UpdateRemoteFileOutboxWorker` before calling

Before calling `PackageFileStreamsAsync`, copy the header and overwrite `header.FileMetadata.Payloads` with only the descriptors whose op is `AppendOrOverwrite`. Same effect on the wire, much smaller diff. Caveat: the recipient still needs the full descriptor list in `metadata.Payloads` to know the post-update file state, so we'd need to send the full list in the metadata blob but only ship streams for the filtered subset. Slightly delicate.

Recommendation: **Option 1**. It expresses the contract directly in the type, and is harder to silently regress.

### Side benefit

Once option 1 is in place, peer chat-update bandwidth drops substantially. Today, every reaction / edit / read-receipt on a message-with-attachment re-uploads the full image + thumbnails to every connected recipient. After the fix, those updates carry only the metadata + transfer key header.

---

## Defence-in-depth already in place

To catch regressions of this class going forward, an `InboxOrphanScanBackgroundService` has been added (tenant-scoped). It walks the inbox staging dirs daily, correlates per file with `LastSeenService` (only flags as orphan when the tenant has been online long enough since the file landed to have processed it), and logs at error level. Detection only — no deletion. This will surface any future contract drift between sender, recipient commit, and recipient cleanup.

---

## File pointers

- Sender bug: `src/services/Odin.Services/Peer/Outgoing/Drive/Transfer/Outbox/OutboxWorkerBase.cs:167-205`
- Recipient cleanup fix: `src/services/Odin.Services/Drives/DriveCore/Storage/InboxStorageManager.cs:75`, `src/services/Odin.Services/Peer/Incoming/Drive/Transfer/FileUpdate/PeerFileUpdateWriter.cs` (both return paths)
- Op-type enum: `src/services/Odin.Services/Drives/DriveCore/Storage/BatchUpdateManifest.cs:36-41`
- Commit-side filter: `src/services/Odin.Services/Drives/FileSystem/Base/DriveStorageServiceBase.cs:1080,1098` (only iterates `DeletePayload` and `AppendOrOverwrite`)
- New scan service: `src/services/Odin.Services/Background/BackgroundServices/Tenant/InboxOrphanScanBackgroundService.cs`
