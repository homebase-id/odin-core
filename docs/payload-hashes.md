# Payload hashes (#1895)

## Part 1: Spec

### Purpose
Optional hashes on each payload, computed by the client.
- **Integrity:** the server rejects an upload or peer transfer whose bytes don't match.
- **Clients:** they can verify downloads, detect content changes without relying on mtime, and match files by content (the desktop sync client depends on this).

### Wire format
`PayloadDescriptor` gains one optional property, `hash`. When absent it is **omitted from the JSON** (never `"hash": null`), so headers without hashes are unchanged.

```json
"hash": {
  "algorithm": "blake3",
  "storedHash": "<base64, 32 bytes>",
  "contentHash": "<base64, 32 bytes unencrypted / 48 bytes encrypted>"
}
```

| Field | Meaning |
|---|---|
| `algorithm` | `"sha256"` or `"blake3"` (read also as `1` / `2`). No others. Applies to both hashes |
| `storedHash` | Hash of the bytes as uploaded and stored: ciphertext if the payload is encrypted, plaintext otherwise |
| `contentHash` | Hash of the original plaintext. Encrypted on encrypted payloads (see below) |

Bytes are standard base64, like `iv`. Clients send the same object on the upload manifest's payload descriptor.

### Rules
1. **Optional.** No `hash` → behaviour is exactly as before, unless the drive requires hashes (rule 7).
2. **Complete or absent.** `algorithm`, `storedHash` and `contentHash` are all present, or `hash` is omitted.
3. **Streaming, single pass.** Hashes are computed while the bytes flow, never by re-reading them.
   - Clients hash the plaintext going in and the ciphertext coming out of the same encryption pass.
   - The server hashes the request stream as it writes it to storage.
   - Future features (resumable upload, payload move) must keep this property.
4. **The server verifies `storedHash`** against the received bytes. Mismatch → 400 `PayloadHashMismatch`, and the written bytes are deleted. Match → stored unchanged. Nothing is returned, because the client already has both values.
5. **Unencrypted payloads:** `contentHash` must equal `storedHash`.
6. **Encrypted payloads:** `contentHash` is the 32-byte plaintext hash encrypted by the client with the file's AES key (48 bytes for AES-CBC+PKCS7 and for AES-GCM+tag). The server treats it as opaque and only checks its length. A plaintext hash in the clear would let anyone with server or storage access confirm that a tenant holds a known file.
7. **Drive setting `requirePayloadHashes`** (default off; drives created before it existed read as off). When on, every payload written to the drive must carry a `hash` → otherwise 400 `PayloadHashRequired`. Applies to new upload, update, add-payload and peer receive. Set at drive creation or via owner endpoint `drive/mgmt/set-require-payload-hashes`.
8. **Peer transfer:** the stored bytes are identical over peer, so `hash` travels unchanged. The receiving server verifies `storedHash` (rule 4) and applies its own drive's rule 7.
9. **Scope:** payloads only. Thumbnails are not hashed. Server-generated payloads (profile, contacts) carry no hash.

### Content-hash IV and binding rule
- `contentHashIv = payloadIv XOR 0xAA…AA`, over the full IV length. It is derived, so nothing extra is stored. XOR with a non-zero constant never yields the payload IV itself.
- **Binding rule:** the encrypted content hash is tied to the payload IV.
  - A *different* hash must never be encrypted under an unchanged payload IV: under GCM that is nonce reuse.
  - Hashes are therefore set only together with a payload upload. Changing the algorithm or correcting a hash means re-uploading the payload, which rotates the IV.
  - Any future "attach hash" operation is only allowed on a payload that has no hash yet.

### Errors (HTTP 400)
| Code | Value | When |
|---|---|---|
| `PayloadHashMismatch` | 4175 | received bytes ≠ `storedHash` |
| `InvalidPayloadHash` | 4176 | incomplete, unknown algorithm, wrong length, unencrypted `contentHash ≠ storedHash` |
| `PayloadHashRequired` | 4177 | drive requires hashes and none was sent |

### Golden vectors
Pinned in `tests/services/Odin.Services.Tests/Drives/DriveCore/Storage/PayloadHashTests.cs` and
`tests/core/Odin.Core.Cryptography.Tests/TestContentHash.cs`. Expected values were computed
independently (Python hashlib/cryptography; BLAKE3 from the official test vectors).

| Item | Value (hex) |
|---|---|
| Input | 1025 bytes, byte *i* = `i % 251` |
| SHA-256(input) | `bc0b6b10b89b9487a12fda2a8cc13194e7091c217aabf8b92846274026f4bcd0` |
| BLAKE3(input) | `d00278ae47eb27b34faecf67b4fe263f82d5412916c1ffd97c8cb7fb814b8444` |
| File AES-256 key | `000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f` |
| Payload IV | `3c1f8e5a9b2d47e0a6c4d8f1027b9e35` |
| Content-hash IV | `96b524f03187ed4a0c6e725ba8d1349f` |
| AES-CBC(SHA-256 hash) | `707e52fbded98a6a5f2e2d29efb31bd4a76cc302e70680437b969ddbf2117a5f76efb8fe04637b3eefb16adced53ad6a` |
| AES-CBC(BLAKE3 hash) | `fe9fbd96981ea69f6b15430fab88d92acbb1e1d5c28b6d54f86c5d195f678955259f6ef4ccb89d8520416e0e20fc1c3c` |

A GCM vector is added when payloads move to GCM.

## Part 2: Implementation plan (remove before merge)

### Risk
| Risk | Mitigation |
|---|---|
| `"hash": null` on every descriptor (server serializes nulls) | `[JsonIgnore(WhenWritingNull)]`, pinned by a test |
| Clients parsing strictly | chat-kmp `ignoreUnknownKeys = true`; System.Text.Json ignores unknown fields on peers |
| BLAKE3 dependency | **Blake3.NET 3.0.2** (BSD-2-Clause, fully managed SIMD, no native binaries) behind `IncrementalContentHash`; matches all 35 official vectors. BouncyCastle's `Blake3Digest` was rejected: 86 MiB/s, slower than SHA-256 |
| CPU cost of hashing on the request path | Only when a hash is sent. Measured single thread, i7-8700K (AVX2, no SHA extensions), 64 KiB chunks: SHA-256 506 MiB/s, BLAKE3 1,407 MiB/s |
| A peer payload written straight to long-term can't be cleaned on mismatch by existing code | `WriteIncomingPeerPayload` deletes it explicitly |
| Storage retry re-reads a partly consumed stream | Already broken today (length check); with a hash it surfaces as a mismatch |
| No DB change | Hash lives in `hdrFileMetaData` JSON; ~160 chars per payload against the 60,000 cap |

### Where the single-pass hash happens
| Path | Hashed while… |
|---|---|
| New upload, update, add-payload | the request stream is written to staging (`HashingReadStream` around `WriteUploadStream`) |
| Peer receive (direct write or straight to long-term) | the peer's request stream is written (`DriveStorageServiceBase.WriteIncomingPeerPayload`) |
| Staging → long-term copy at commit | not hashed again (already verified) |
| `PayloadsAreRemote` | not hashed: no bytes here; passed through as-is |

### Status
**Done on the branch (uncommitted):**
- [x] `ContentHashAlgorithm`, `IncrementalContentHash` (SHA-256 built in, BLAKE3 via Blake3.NET), `HashingReadStream` (Odin.Core.Cryptography) + tests passing
- [x] `PayloadHash` (rules, `Verifying`, `ContentHashIv` via `ByteArrayUtil.EquiByteArrayXor`) + `Hash` on the 3 descriptor types and 5 builders
- [x] Verification in the 3 upload writers (descriptor registered before the check, so existing staging cleanup removes the bytes)
- [x] Rules 5–7 in the 3 writers' validation
- [x] Peer receive through `WriteIncomingPeerPayload` (new-file + update)
- [x] Drive flag end to end (details JSON, `StorageDrive`, `DriveManager` setter, endpoint, owner DTOs, comparer)
- [x] Error codes 4175–4177

**To do:**
- [x] `PayloadHashTests` 27/27, incl. the golden vectors
- [x] Integration tests (Odin.Hosting.Tests.V2), 17/17; the upload check and both peer cleanup branches mutation-checked:
  - upload SHA-256/BLAKE3 × encrypted/unencrypted → header returns the hash
  - wrong / incomplete / unencrypted-unequal → right 400, no file left
  - no hash → unchanged
  - update: set, kept, cleared
  - add-payload: ok + mismatch
  - required drive: reject / accept / metadata-only ok
  - peer: hash arrives unchanged; receiver mismatch; receiver required flag
- [x] BLAKE3 throughput measured; swapped BouncyCastle → Blake3.NET
- [x] Release build `--warnaserror` (CI define constants)
- [ ] Full V2 + services + hosting test projects; CI SQLite + Postgres
- [ ] `/simplify` reminder before PR

### Follow-ups (not in this PR)
- Payload move: verify by hashing while fetching (wrap the fetch stream, single pass), not by re-reading the temp file.
- Resumable upload: keep the hasher across chunks in memory; never re-read the staged file (rule 3).
- Optional hardening: reject a reused payload IV on overwrite (binding rule).
