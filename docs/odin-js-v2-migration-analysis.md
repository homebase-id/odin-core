# odin-js: V1 → V2 API migration analysis

Date: 2026-10-30. odin-core `main` @ `d47c3e9ef`, odin-js `main` @ `2a493e61`. Read-only analysis; nothing changed.

Legend: **[code]** = read in source. **[inferred]** = reasoning, not verified at runtime.

## Bottom line

- odin-js calls about **270 distinct V1 identity endpoints** (plus 4 hand-built media URL builders and the WS URLs). **[code]**
- V2 covers the **data plane** well: drive read/query/write, reactions, peer reads/sends, connections, follow, contacts, notification list, push, links, plan. That is about **110 of the 270**, and it is the part used by every app (feed, community, owner, public). **[code]**
- V2 has **nothing** for the **control plane**: owner login/password, YouAuth + app registration, drive management, recovery/Shamir/ZK, settings/EULA/data-conversion/DNS, CDN publish, `/pub/*`, home auth. That is about **130 endpoints**, mostly owner-app. **[code]**
- So a full migration is **two projects**: (1) move odin-js's data-plane calls to the existing V2 (client work plus a few server gaps), and (2) build V2 for the control plane in odin-core first. Project 1 is tractable. Project 2 is the bulk of the server work, and it is optional: those endpoints are owner-console-only and could stay V1 for a long time.

## Cross-cutting changes (needed before any endpoint moves)

| # | Change | Where |
|---|---|---|
| 1 | **App token transport.** V1 apps send the CAT in a `BX0900` *header*. V2 does not read that header; it reads `Authorization: Bearer`, or the `BX0900` cookie. **[code]** `UnifiedAuthenticationHandler.cs:209,227` | js-lib `DotYouClient.createAxiosClient` |
| 2 | **One base URL.** `/api/v2` for every ApiType; owner/app/guest is decided by the token, not the path. `getEndpoint()` switch goes away for V2 calls. **[code]** | `DotYouClient.ts` |
| 3 | **Drive addressing.** V2 routes take `driveId` in the path. `driveId == TargetDrive.alias` (`DriveManager.cs:166`), so no lookup is needed; `type` is dropped. **[code]** Every provider that builds `alias&type` query strings changes. | ~30 js-lib providers |
| 4 | **Shared-secret encryption** is the same middleware and wire format (`?ss=`, `{iv,data}`). V2 opts out per action (`[NoSharedSecretOnRequest]`): payload/thumb GETs, multipart, peer send. The interceptors keep working; only `overrideEncryption` sites change. **[code]** | `DotYouClient.ts` |
| 5 | **File-system type** still via `X-ODIN-FILE-SYSTEM-TYPE` header or `xfst` query. No change. **[code]** | — |
| 6 | **Response shapes.** Upload/update return flattened `CreateFileResult`/`UpdateFileResult` (`fileId`, `driveId`, …) instead of `UploadResult.file.targetDrive`. Batch-collection sections get per-section `status`/`errorMessage`. Header 404s instead of empty. **[code]** | upload + query callers |
| 7 | **Media URLs.** V2 payload/thumb routes are path-based and unencrypted (`/files/{fileId}/payload/{key}/thumb/{w}/{h}`), which is *easier* than V1's `?ss=` URLs, but a browser `<img>/<video>` can't send a Bearer header, so **app clients still can't stream encrypted media by URL** unless a cookie or CDN token is used. **[inferred]** | `MediaProvider`, `ExternalVideoProvider`, `useHlsManifest` |

## Area-by-area

| Area (odin-js count) | V2 status | Client work | Server gap |
|---|---|---|---|
| Drive query/read (14) | Mostly full | Re-path; body drops `targetDrive` | No local **header-by-gtid**; no **query-modified**; no `metadata/type` (use `/apps/{slug}/drives?type=`); `qbc` home cache |
| Drive write (9) | Full | Re-path; new result shape; `overwriteFileId` → PATCH | — |
| Payload/thumb/media (4 builders) | Full routes | Rewrite URL builders | **Local full-payload routes ignore the `Range` header** (`V2DriveFileReadonlyController.cs:185`, `chunk = null`); V1 honours it. Breaks `<video>` seeking. **[code]** |
| Local reactions + group reactions (8) | Full (+ `toggle`) | Re-path | — |
| Reactions on a **remote** identity's file (`/transit/reactions/*`, 5) | **None** | — | Need peer reaction routes |
| Peer reads (9) | Partial | Re-path, `odinId` in route | No peer **query-modified**, **header-by-uniqueId** (only `exists`), peer `metadata/type`, peer **security context** |
| Peer send / delete / inbox (3) | Full | Re-path | — |
| Connections, requests, intros, block, troubleshooting (~30) | Full, REST-ified | Rewrite to new verbs/paths | — |
| Circle definitions (9) | Partial | — | No **get-single**, **update**, **set-owner/reassign-owner**; V2 `create` is apps-only |
| Follow (8) | Full (kebab-case paths) | Re-path | — |
| Contacts (6) | Full | Re-path | — |
| YouAuth domains (7) | **None** | — | Owner-only; stays V1 or needs routes |
| Security context (2) | Full (`/auth/context`) | Re-path | — |
| App/guest auth (`verifytoken`, `logout`) | Full | Re-path | V2 logout deletes the registration but **doesn't clear cookies or expire owner tokens** (`V2AuthController.cs:51-57`) **[code]**; owner logout must stay V1 **[inferred]** |
| YouAuth authorize/token, `auth/ident`, home auth callback | **None** | — | Navigation/redirect flows; probably stay V1 |
| Notification list (8) | Full but **broken route** | Re-path to `/api/v2/notify/push/list*` | **Route collision**: `V2NotificationListDataController` and `V2AppPushNotificationController` are both at `/api/v2/notify/push` and both inherit `[HttpGet("list")]` **[code]**; GET will throw an ambiguous-match at runtime **[inferred]**. Fix before migrating. |
| Push subscriptions (7) | Full | Re-path | — |
| Websocket (owner/app/peer) | Partial | App: flip existing `useV2` flag | V2 socket accepts **app tokens only**; owner console socket has no V2 **[code]** |
| Drive management (15) | **None** | — | Owner-only |
| App registration/management (14) | **None** | — | Owner-only |
| Owner login/password/salts/nonce (13) | **None** | — | Owner-only |
| Recovery/Shamir/ZK/account (43) | **None** | — | Owner-only |
| Settings/EULA/data-conversion/DNS/mail (15) | **None** (mail has a different V2 shape) | — | Owner-only |
| CDN publish, `/cdn`, `/pub/*`, public keys, `/api/v1/version` | **None** | — | Public/static; probably fine to leave |
| Link preview, plan | Full | Re-path (plan already V2) | — |

Already on V2 today: owner-app `/api/v2/plan`, the ZK shard-recovery public-key peer call, web-drop-app's anonymous by-uid reads, and an opt-in (unused) V2 app websocket. **[code]**

## Where the client work lives

- **js-lib** holds ~160 of the endpoints across ~30 provider files. Apps call js-lib functions, not raw paths, so the data-plane migration is mostly contained in js-lib. **[code]**
- **owner-app** has ~95 raw paths in its own providers. Almost all are control-plane endpoints with no V2 equivalent. **[code]**
- **common-app** ~14, **ui-lib** 1 (HLS), the other apps a handful of redirects. **[code]**

## Suggested order

1. **Server fixes on V2 first** (small): the `/notify/push/list` collision; honour `Range` on local full-payload routes.
2. **js-lib transport**: Bearer header for apps, a V2 base URL, `driveId` addressing helper. Keep V1 alongside; migrate per provider.
3. **Migrate the covered data plane** in js-lib: drive read/write, reactions, connections/follow/contacts, notifications/push, security context, app websocket (`useV2`).
4. **Fill server gaps the apps actually hit**: peer reactions, query-modified (local + peer), header-by-gtid, peer header-by-uid, circle-definition get/update.
5. **Decide on the control plane** (owner-app admin, auth, recovery): either build V2 controllers or declare it V1 indefinitely. This is the largest piece and the one with no client benefit until the server exists.

## Open questions

- Is the goal to *retire* V1, or just to get the apps (feed/community/public) onto V2? The answer decides whether step 5 happens at all.
- How do app clients load encrypted media by URL once the `BX0900` header is gone? (Cookie, CDN token, or keep fetch-to-blob.)
