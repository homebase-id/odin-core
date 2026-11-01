# Homebase security model

A map of who may do what, and how keys back it up. Read it before touching any permission check.

Markers: **(V)** verified in the code (file named), **(?)** intent the code doesn't state. Ask the owner of
the design before acting on a (?).

Deeper docs: [app-circle-membership-plan.md](app-circle-membership-plan.md) (key model, deposits),
[app-circle-membership-status.md](app-circle-membership-status.md), [archive/drive-addressing.md](archive/drive-addressing.md)
(the slug design's full argument; the facts are in section 3), [drive-deletion-and-app-uninstall.md](drive-deletion-and-app-uninstall.md),
[connection-defaults.md](connection-defaults.md), [autoconnected-security-level-audit.md](autoconnected-security-level-audit.md),
[youauth-unified-authorization.md](youauth-unified-authorization.md), [weak-key-retirement.md](weak-key-retirement.md),
[zero-knowledge-drives.md](zero-knowledge-drives.md) (direction: sharing models, keys only on devices),
[owner-keys.md](owner-keys.md) (every owner key and what locks it, with the shorthand we write them in).

## 1. The platform

- Homebase is a generic self-sovereign **app platform**. One identity = one domain = one tenant, with its own server.
- Apps authenticate with **YouAuth**, an OAuth2-like flow: the owner consents to what the app asks for, and each device
  gets a client token.
- Apps own **drives** and **circles** (section 2). Each drive is encrypted with its own **storage key** (section 8).
- An app sees only what it was granted: "an app gains nothing beyond its own reach". This is cryptographic, not just a
  check: no storage key in hand, no working grant (`PermissionContextStorageKeySource`) **(V)**.
- All files are zero knowledge encrypted at rest. No decryption keys are stored on the server, they only appears as in-memory blips when the user is online and calls the API.
- **Zero-knowledge drives** (today only Shard Recovery) have no master-key copy of their key: the only copy is locked
  by the owner's zero-knowledge master key, which only the browser opens (`StorageDrive.IsZeroKnowledge`). The server
  never encrypts for one, and no grant on one carries its key (`StorageDrive.GrantCarriesStorageKey`) **(V)**.
- **Recovery in the browser** ([zero-knowledge-recovery-plan.md](zero-knowledge-recovery-plan.md)): once the browser
  takes the recovery phrase over, the server holds no copy of it it can open (`RecoveryKeyRecord.TakenOver`). The
  phrase locks the zero-knowledge master key and the private half of a phrase key pair; the password is sealed to the
  public half, so a login can write it but only the phrase reads it. The phrase recovery read
  (`authentication/zk-phrase-recovery`) needs **no login**: it returns only those locked values, useless without the
  128-bit phrase. The handover, the takeover and the password copy need proof of the password itself, as a password
  change does (`PasswordReply`, `ZeroKnowledgeRecoveryService`), so a stolen session without the password can do none
  of them. Someone *with* the password can still seal junk the server can't open or check, so recovery doesn't rely on
  it alone: the server locks today's master key with the phrase's reset key, hkdf(phrase), itself
  (`ResetKeyEncryptedMasterKey`), and `authentication/zk-phrase-reset` (no login, the key encrypted to the host)
  resets the password with it and ends every owner session. Nothing the password opens reads the copy or makes the
  reset key. Server-dealt Shamir locks the master key, not the phrase. **(V)**

### Callers

| Caller | Authenticates with | Gets its keys from |
|---|---|---|
| Owner | password → master key | master key unwraps everything |
| App (owner using an app) | client token | token half ⊕ server half → App Client Key → **App Key** → its drive storage keys (+ ICR key if it asked for a transit key) |
| Guest / YouAuth domain | client token | its own grant (`AppGrants`) |
| Peer (another identity) | TLS client cert + CAT | CAT → **Peer Key** → that connection's circle grants |
| Anonymous | nothing | drives with `AllowAnonymousReads` only |

- The App Key and Peer Key are hub keys: each grant beneath them is wrapped once, individually (plan doc) **(V)**.
- An app gets the ICR key only when it requests a transit permission; guests have none **(V)**, from the #1977 work.

### Permission keys (`CirclePermissionFlags.cs`, class `PermissionKeys`) **(V)**

`ReadConnections` 10, `ReadConnectionRequests` 30, `ReadCircleMembership` 50, `ManageCircleMembership` 51,
`ReadWhoIFollow` 80, `ReadMyFollowers` 130, `ManageFeed` 150, `ManageContacts` 160, `ManageProfile` 170,
`UseTransitWrite` 210, `UseTransitRead` 305, `SendPushNotifications` 405, `PublishStaticContent` 505,
`SendOnBehalfOfOwner` 707, `AllowIntroductions` 808, `SendIntroductions` 909. Some imply others (`Implications`).

## 2. Apps

Everything is an app. Drives and circles exist because an app owns them. Every drive has an owning `AppId`; owner
drives belong to the owner console (`StorageDriveData.AppId`) **(V)**.

### Three kinds

- **System apps** have special APIs:
  - **Contacts** owns the contact and profile drives and their circle (Friends) (`BuiltinApps.Builtin`) **(V)**.
    A Homebase with just Contacts and the system drives is viable.
  - **Recovery** is the app side of Shamir password recovery. It owns the shard drive (`ShardRecoveryDrive`), which the
    Shamir services use (`ShardRequestApprovalCollector`). Its circle, Recovery, holds the people who keep a shard: it grants
    Write only on that drive, and nobody joins it without the owner's review (`GrantOn = Review`) **(V)**.
- **System drives** belong to the System app, which is the owner console (`SystemAppId`, e.g. the transient drive) **(V)**.
- **Default pre-installed apps:** Chat, Email, Feed, HomePage, Location (Emergency Location Access), Mail, Community,
  Moments, Webdrop and Vault. They are only there so the user doesn't have to install them one by one.
  - The code's seeded list (`BuiltinApps.Builtin`) doesn't match yet: Community is only "well-known" (not seeded), and
    Mail isn't on the tree at all, though its drive is created (`BuiltinApps` remarks) **(V)**.
- **Third-party apps:** anything else the owner installs.
- `AppUninstallService.IsBuiltIn` (the owner console + every app on the tree, plus Lists and Mail) can't be uninstalled **(V)**.

### How a client installs an app (YouAuth, `youauth-unified-authorization.md`) **(V)**

1. The app starts YouAuth (`/authorize`, `client_type=app`) with its `permission_request`.
2. Not registered yet → the identity redirects to the owner console's `/owner/appreg` with the app's parameters [045].
3. The request is an `AppRegistrationRequest`:
   - `AppId`, `AppSlug` (required, first-come, immutable), `Name`, `CorsHostName`;
   - `PermissionSet`: the permission keys the app wants;
   - `Drives`: drive grants, each with `driveMeta` to create the drive if it's missing. For example, two drives;
   - `Circles`: circles the app declares and will own (#1939);
   - `AuthorizedCircles` + `CircleMemberPermissionGrant`: existing circles whose members may use the identity through
     this app, and what they get. Each must be a circle this identity has: an unknown id, or one of the retired
     Confirmed Connections / Auto Connections circles (deleted by #1809), is refused with 400
     (`AppRegistrationService.AssertCirclesExistAsync`, #2001). Authorizing a circle with no members would grant
     nothing, silently. Updating the authorized circles checks only the ones added, so a registration that already
     holds a circle that is gone still saves **(V)**.
4. The owner approves [047]. The console then:
   - creates each missing drive with the installing app as owner (`useApp.ts ensureAppOwnedDrives`, odin-js). It never
     creates a well-known drive for an app that doesn't own it; that grant is left for the server to refuse;
   - registers the app.
5. The server checks the declared circles **before** saving the registration, bounded by what the registration grants
   (`CircleDefinitionService.AssertDeclaredCirclesAsync`), and creates them after it. A refused circle refuses the install.
6. Consent [050–055], then the token exchange gives the device its client token and shared secret.

Later:
- **Extend permissions** (owner console `ExtendAppPermissions`) adds drives at runtime, e.g. one per feed channel.
- **Owned circles at runtime:** an app may create circles, but only granting permission keys and drive access it holds
  itself. On a drive it doesn't own it may grant Read only, and only with the storage key
  (`CircleMembershipService` → `AssertAppMayGrantDrivesAsync`) **(V)**. An app's grant on a zero-knowledge drive
  carries no storage key (its devices get the key only from the owner's browser, below), so Read on one it doesn't
  own is refused **(V)**.
- **Zero-knowledge app key:** an app that asks for it at sign-in (`zk_app_key=1`) gets its zero-knowledge key sealed by
  the owner's browser at consent, and its security context carries each zero-knowledge drive's key locked with it, for
  the drives it is granted reading (`AppRegistrationService.GetZkDriveKeysAsync`) **(V)**. A device counts as holding
  those keys only while the key it was given is still the app's (`AppClientRegistration.ZkAppKeyId`) **(V)**. A reset
  with today's master key keeps every app's key and copies: the devices holding them may be the only way left into
  the drives (`ZeroKnowledgeKeyService.DeleteAsync`) **(V)**. See `zero-knowledge-drive-api-plan.md`, step 3.
- **Uninstall** can delete the app's drives and circles (`drive-deletion-and-app-uninstall.md`) **(V)**.

## 3. Drives

### Drive permissions (`DrivePermission.cs`) **(V)**

| Bit | Meaning |
|---|---|
| `Read` 1 | read files |
| `Write` 2 | write files, **without** read. Deliberate: in chat you can receive, nobody can read |
| `React` 4 | add reactions |
| `Comment` 8 | write comments |
| `ConditionalTemporalRead` 16 | read only recently modified files, only via the temporal API; not part of `ReadWrite`/`All` |

- **Write-only is cryptographic**: a writer never holds the storage key **(V, plan doc)**. Example: Chat.
- **TO-DO** Allow / block enumerating files on a drive (block queryBatch) for web-drop like apps.

### Drive options (`StorageDriveDetails`) **(V)**

| Option | Effect |
|---|---|
| `OwnerOnly` | every file must carry the Owner ACL (`FileSystemStreamWriterBase`, `FileSystemUpdateWriterBase`); can't be combined with anonymous reads or subscriptions (`DriveManager`) |
| `AllowAnonymousReads` | anyone may read files whose ACL allows it; also relaxes the comment checks (section 6) |
| `AllowSubscriptions` | followers may subscribe (feed); collaborative channels need it |
| `AllowCdn` | the CDN may read payloads (`IsCdnEnabled`); anonymous drives are reachable anyway |
| `RequirePayloadHashes` | every payload must carry client hashes |
| `IsReadonly` | documented "owner only, in owner auth". **No write path reads it** (grep) **(V)**, so it's not enforced |
| `IsArchived` | non-owner callers are refused (`AssertDriveIsNotArchived`) |
| `WriteOnlyKeyPair` | per-drive ECC-384 key: seal a deposit without being able to read; private half under the storage key. Handed only to callers with Write |
| Attribute `IsCollaborativeChannel` | collaborative feed channel (`IsCollaborationDrive`) |

- System drives (`BuiltinDrives.Protected`) can't be deleted **(V)**.
- Tenant settings add rights on anonymous drives for authenticated and connected identities: Read always,
  Comment/React by flag (`TenantSettings.GetAnonymousDrivePermissionsFor…`) **(V)**.

### Drive addressing (slugs)

A drive has two names: `DriveId`, the internal handle, and `(AppSlug, DriveSlug)`, the portable one a remote caller
uses on another identity. The original design, with its full argument, is archived in
[archive/drive-addressing.md](archive/drive-addressing.md).

- **Routes** (`UnifiedApiRouteConstants`) **(V)**: `/api/v2/apps/{appSlug}/drives/{driveSlug}/…` and
  `/api/v2/peer/{odinId}/apps/{appSlug}/drives/{driveSlug}/…`, beside the Guid routes
  (`/api/v2/drives/{driveId}/…`). Authorization is unchanged: the slug replaces only the address, and a caller still
  needs the drive grant.
- **Listing** `GET /api/v2/apps/{appSlug}/drives?type=` (`V2AppDriveController`) **(V)** returns only drives the caller
  can already reach: its own, plus cross-app grants. The owner console sees all.
- **A drive's public key** (`…/drives/{driveSlug}/public-key`, `V2PeerAppDrivePublicKeyController`) **(V)** is served
  only to a caller with Write on that drive; anyone else gets a security exception, not a 404, since resolving the
  address already showed the drive exists. So a stranger with no grant can't deposit yet
  ([weak-key-retirement.md](weak-key-retirement.md)).
- **Slug format** (`OdinSlug`) **(V)**: `^[a-z0-9]([a-z0-9-]*[a-z0-9])?$`, 1–14 characters, for `AppSlug`, `DriveSlug`,
  `DriveTypeSlug` and circle slugs alike, plus a reserved list (empty today) for route segments. That is RFC 3986
  *unreserved* narrowed to an RFC 1123 host name label, so a slug never needs escaping in a URL, HTML or JSON.
  `OdinSlug` is a value type like `OdinId`, so an invalid one can't exist. **Always lowercase**: upper case is
  lowercased when a slug is made, so `Chat` and `chat` are one slug, and comparing a slug with a string lowercases
  the string. Anything else malformed is **refused, never changed**: a slug is immutable and ends up in other
  identities' URLs, so stripping a character would make an address the caller didn't ask for. A malformed slug is a
  400 `argumentError` in a body, route or query.
- **Uniqueness:** `AppSlug` per identity, `DriveSlug` per app (`UNIQUE(identityId, AppId, DriveSlug)`), all `NOT NULL`
  since #1901 **(V)**.
- **Slugs are resolved by the recipient.** `/peer/{odinId}/apps/chat/…` means whatever app *that identity* registered
  as `chat`. An `AppSlug` is a package name, first come first served per identity, with **no global registry**: a
  different app can register as `chat` on an identity where the real one isn't installed. It still needs a grant to
  be written to, so this is a naming risk, not an access one. Interop is by referencing another app's drive, never by
  taking its name.

Why it is built this way:
- **The tree is rooted at `/apps`**, not under `/drives`, so every segment names what follows it (`/drives/feed`
  would read as "the drive called feed"), and slugs could be added without touching the Guid routes.
- **The type stays out of every unique key:** an app may own many drives of one type (the feed app's channels),
  told apart by `DriveSlug`. `DriveTypeSlug` is the type's readable form, kept on `Drives` beside the type Guid
  rather than in a registry table: drives number in the tens, and a single code path writes them.
- **`AppRegistrations` is a real table**, because `UNIQUE(identityId, AppSlug)` can't be expressed over the old
  key-value blobs, and a slug other identities resolve against can't be "unique, best effort".
- **`detailsJson` exists from the start**, unused, because a nullable column costs nothing now and a migration
  across both databases later.
- **A circle's `AppId` is a column, not a field in its JSON**, so "delete the app's circles" and the ownership check
  on every app write are one indexed query rather than a parse of every circle.

Not built from that design: validating the type pair at drive creation (#2007); retiring `metadata/channel-drives`
(#2008); the feed services still read the global channel drive type rather than `AllowSubscriptions` (#2009).

## 4. Circles

Circles are a consequence of apps: each pre-installed app brings its circle (Chat → Chat, Feed → Feed,
Location → Emergency Location Access, Contacts → Friends, …; `BuiltinApps`) **(V)**.

- A circle is a set of identities plus what membership grants: `DriveGrants` and `Permissions` (`CircleDefinition.cs`) **(V)**.
- A connected caller's rights are the union of their enabled circles' grants. A disabled circle grants nothing **(V)**.
- `AppId` on a circle is the owning app; null = owner circle. An app may change only its own circles and can't enrol
  anyone into an owner circle **(V)**.
- **Read grants need the Peer Key** (the drive's storage key is wrapped for the member). Write/react grants, and every
  grant on a zero-knowledge drive, carry no key material (`CircleNetworkService.RequiresPeerKeyAsync`, which asks
  `StorageDrive.GrantCarriesStorageKey`) **(V)**. So an app *deposits* a read grant (`DepositedGrant`), and it converts to a real
  grant next time the Peer Key is in scope **(V)**.
- **No confused deputy:** an app can't plant a circle naming a drive it can't read, which the owner's next grant would
  otherwise make work ([archive/drive-addressing.md](archive/drive-addressing.md#circles)). Creation is bounded by what the app holds (section 2), and updating a
  circle definition needs the master key (`CircleMembershipService.UpdateAsync`) **(V)**.
- Unreviewed connections are treated as `Authenticated`, not `Connected`, when content is evaluated
  (`ReviewedSecurityTier`) **(V)**.

### Enrollment: when a circle grants itself (`CircleGrantOn`) **(V)**

Details in [connection-defaults.md](connection-defaults.md).

| `GrantOn` | Granted |
|---|---|
| `None` | never automatically: manual membership. The default |
| `Connect` | at any connection, ambient introductions included |
| `OwnFlowConnect` | only when the connection is made through the owning app's own consent flow (the vendor case) |
| `Review` | when the owner completes the connection review |

- **Deposit-only invariant:** a `Connect` or `OwnFlowConnect` circle may grant only write/react on drives, no read
  beyond anonymous drives, and no permission keys. Checked when the definition is written
  (`CircleDefinitionService.AssertDepositOnlyIfAmbientAsync`), because read grants need the key ceremony the review is
  for **(V)**.
- **Membership implies review:** a manual or review-granted circle refuses an unreviewed contact, and the review
  can't be cleared while the contact is in a personal circle (`CannotClearReviewWhilePersonalCircleMember`) **(V)**.
- Anyone but the owner sees a connection redacted to the identity list (`RedactedForExternalViewer`) **(V)**.

## 5. Files and their ACL

- Each file carries an `AccessControlList`: `RequiredSecurityGroup` + optional `CircleIdList` and `OdinIdList` **(V)**.
- Security groups are a ladder: `Anonymous` 111 < `Authenticated` 444 < `AutoConnected` 555 < `Connected` 777 <
  `Owner` 999. A caller passes every rung at or below their own (`DriveAclAuthorizationService.Allows`) **(V)**.
- Named circles and named identities narrow further, together: the caller must be in at least one listed circle **or**
  be one of the listed identities, and still meet the security group, so a named identity that is unconnected or
  unreviewed does not pass a `Connected` ACL **(V)**. Queries apply the same rule in SQL (`aclAnyOf`) **(V)**.
- `OdinIdList` is stored canonical and de-duplicated by `AccessControlList.Validate`, and is cleared before a file is
  sent to a peer, so a recipient never learns who else was named **(V)**. `System` (1) is a caller level, never a file
  ACL **(V)**.
- Owner and `System` callers skip the ACL **(V)**.
- Reading a file needs **both** drive Read and the ACL. Past drive Read, one rule decides whether the caller may see a stored
  file (`IDriveAclAuthorizationService.CallerMayReadAsync`): the ACL, and a client-locked file (sealed or zkDrive) only for a caller holding the
  drive's key, except a deposit staged on the transient drive (`ZkDriveFiles.VisibleTo`). A read by id, a query and a socket event all ask it **(V)**. Queries also drop
  encrypted files the caller has no storage key for (`DriveQueryServiceBase` filter) **(V)**.
- `LocalAppMetadata` never leaves the identity.

## 6. Comments, reactions and other special file kinds

- **Comments are a separate file system** (`FileSystemType.Comment`) on the same drive **(V)**:
  - a comment must reference a file (`ReferencedFile`); its `GroupId` is forced to that file's global transit id
    (`CommentStreamWriter`) **(V)**;
  - the author is the caller's OdinId, so anonymous callers can't comment **(V)**;
  - write needs `Comment`; read needs `Read` or `Comment` (`CommentFileStorageService`) **(V)**;
  - **on a drive with `AllowAnonymousReads` both checks are skipped** **(V)**. Whether the tenant "can comment" flags are
    still enforced elsewhere: **not checked** **(?)**;
  - telling recipients to delete a comment accepts either transit key; other file deletes need `UseTransitWrite`
    (`AssertCanTellRecipientsToDelete`, #1978) **(V)**. Why comments are looser: **(?)**.
- **Reactions** need `React` on the drive (`ReactionContentService`, `GroupReactionService`) **(V)**. Their peer rules are
  their own; don't fold them into the file rule (user decision, #1977).
- **Read receipts** (transfer history): own rules, hands off (user decision).

### Peer file operations (#1977)

- Plain file ops on a peer: get/query = `UseTransitRead`; send/update/delete = `UseTransitWrite`
  (`PermissionContext.AssertCanUsePeer(PeerAccess)`) **(V)**.
- A write-only grant may never read a peer **(V)**.
- Everything else (comments, reactions, receipts, introductions, connections, live relay, notification tokens, Shamir)
  keeps its own rule. Don't centralize those.

## 7. The file (HomebaseFile)

What a client gets back for one file (`SharedSecretEncryptedFileHeader`; odin-js calls it `HomebaseFile`) **(V)**:

```
fileId, targetDrive, fileState, fileSystemType, priority, fileByteCount
sharedSecretEncryptedKeyHeader   the file's KeyHeader, wrapped for this caller (section 8)
fileMetadata
  globalTransitId, referencedFile (comments), created, updated, senderOdinId, originalAuthor
  isEncrypted, versionTag, payloads[], dataSource, ttl
  appData          uniqueId, tags, fileType, dataType, groupId, userDate, archivalStatus,
                   content (≤ 10 KB), previewThumbnail
  localAppData     versionTag, iv, content (≤ 4 KB), tags, readTime, localReactions
  reactionPreview  the ReactionSummary
serverMetadata
  accessControlList, allowDistribution, doNotIndex, originalRecipientCount
  transferHistory  per recipient: status, in outbox, read time
```

- **`versionTag`** is the file's optimistic-concurrency token. Every header write gives it a new one, and a write that names
  a stale tag fails with `VersionTagMismatch` (`AssertVersionTagMatches`) **(V)**. Compare `updated` too, not only the
  tag (#1967).
- **Three parts don't follow that rule.** They have their own update paths, which write their own column and advance
  `modified` (`updated`), but leave `versionTag` alone (`TableDriveMainIndex`) **(V)**:
  - **`localAppData`** has **its own `versionTag`**, checked by compare-and-set on `hdrLocalVersionTag`. It's for
    client-side sync only and is never propagated to peers. A normal header write doesn't overwrite it;
  - **`reactionPreview`** (`UpdateReactionSummary`) doesn't advance the file's `versionTag`. A header write carries the
    existing summary forward, so an edit doesn't wipe the reactions;
  - **`transferHistory`** (`UpdateTransferHistory`) doesn't either.
- So a client that caches by `versionTag` alone misses reaction, read-receipt and local-data changes. It has to watch
  `updated` (or the change notifications) for those.
- Who may write them **(V)**:
  - the reaction summary needs `React`, `Comment` or `Write`;
  - transfer history needs read or write on the drive;
  - local app data needs write.

## 8. Envelope encryption

**The rule.** A file has one key header (FileKey and an IV); content, local app data, payloads and
thumbnails are all encrypted with FileKey, each with its own IV **(V)**. Only the key header is locked:
- **Normal drive:** by the server with the drive's key (DrvK, `type: aes`). A peer's key header arrives
  wrapped in the connection's shared secret and waits in the drive's inbox until the owner or an app is
  signed in; the server then re-locks it with the keys that caller lends it (`PeerInboxProcessor`) **(V)**.
- **Zero-knowledge drive:** by a client, and the server opens neither kind: by a device holding DrvK
  (`type: zkDrive`), or sealed to the drive's write-only public key by anyone with Write (`type: sealed`,
  a deposit) **(V)**. A key holder may later transform `sealed` into `zkDrive` (designed, built with
  Chat). One exception writes server-side: on a private Email drive the server seals the OpenPGP keyring and its
  pointer to the drive itself (`EmailKeyMaterialWriter`), skipping the write rules a client's upload passes
  (`OpenForWrite`, `AssertMayReplace`), so a rotation's sealed pointer replaces a `zkDrive` one on an upgraded drive;
  it refuses a caller that doesn't hold the drive's key **(V)**. One read rule (`IDriveAclAuthorizationService.CallerMayReadAsync`) shows a client-locked file
  only to a caller that can open it; the model and decisions are in
  [zero-knowledge-drives.md](zero-knowledge-drives.md#how-a-zero-knowledge-drive-holds-files), the work in
  [zero-knowledge-drive-api-plan.md](zero-knowledge-drive-api-plan.md).

Where this is heading (server never sees a key): [zero-knowledge-drives.md](zero-knowledge-drives.md). The owner's
own keys above the storage key (password, master key, zero-knowledge master key, recovery phrase, reset key, Shamir),
and what each one locks: [owner-keys.md](owner-keys.md).

```
master key ──► storage key (one per drive; MasterKeyEncryptedStorageKey)
App Key / Peer Key ──► storage key (one wrapping per grant)
storage key ──► KeyHeader (per file: random AES key + IV; stored as EncryptedKeyHeader)
KeyHeader ──► payloads, thumbnails, appData.content (AES)
```

- On upload the client sends the KeyHeader wrapped in its **shared secret** (derived from the YouAuth login); the server rewraps it with the storage key
  (`DriveStorageServiceBase`, `EncryptKeyHeaderAes`) **(V)**. Reads return it wrapped in the caller's shared secret.
- **Transit:** the sender wraps the KeyHeader with the shared secret of the CAT the recipient issued
  (`CreateTransferInstructionSet`); the recipient unwraps it from the ICR (`PeerInboxProcessor.DecryptedKeyHeaderAsync`)
  and rewraps it with its own storage key **(V)**. Payload bytes are not re-encrypted **(inferred)**.
- `IsEncrypted = false` files use an empty KeyHeader and are stored in clear **(V)**.
- `StorageDrive.AssertValidStorageKey` checks a key against the drive's encrypted id before use **(V)**.
- A zero-knowledge drive takes only key headers an owner device locked with its key (`zkDrive`), and
  deposits. It refuses one locked with the shared secret by its type, before opening it
  (`ZkDriveFiles.OpenForWrite` → `DriveIsZeroKnowledge`) **(V)**. Reads return a `zkDrive` key header as
  stored, only to a caller holding the drive's key: the owner console, or an app device given the app's
  zero-knowledge key (`ZkDriveFiles.CallerHoldsDriveKey`) **(V)**; and a payload or thumbnail read sends no
  `SharedSecretEncryptedHeader64`: the client uses the file key and the payload's IV **(V)**.
- The key header format (`EncryptKeyHeaderAes`) encrypts `IV ‖ key` with AES-CBC under that same IV, so
  the IV cancels out of the first block: the 48 bytes depend only on the locking key and the file key,
  and the IV the reader gets back is the visible `Iv` field **(V, `ZkDriveFileTests`)**. Two key
  headers for the same file key show it, and whoever stores a key header can change its IV. Key header
  version 2 fixes the first ([#2024](https://github.com/homebase-id/odin-core/issues/2024)).
- **Tampering by whoever stores the data is out of the threat model** (decided 2026-11-01): content
  (`appData.content`, payloads, thumbnails) is AES-CBC with no MAC, so changed bytes decrypt to changed
  data without an error, and a changed IV flips bits in the first block. Someone who can change stored
  bytes could as well delete them or hold them for ransom. A later move of file encryption to GCM would
  bring most of it.
- Ciphers: AES-CBC in `KeyHeader`, AES-GCM preferred for newer paths (CLAUDE.md, `youauth` docs).
- Write-without-read keys: drive `WriteOnlyKeyPair` (data deposits) and `PeerKeyStore.WriteOnlyKeyPair` (grant deposits)
  **(V)**.

## 9. System APIs may bypass the regular checks

Shamir recovery (the Recovery app), introductions, connections (requests, verification) and contacts are **system APIs**. They do what
they must regardless of the caller's ordinary permissions. Don't "fix" them to use the file rule.

How they bypass **(V)**:
- `GetIcrAsync(..., overrideHack: true)` reads a connection without `ReadConnections`
  (`CircleNetworkIntroductionService`, `CircleNetworkService`, `CircleNetworkRequestService`, `ShamirRecoveryService`,
  `CircleNetworkVerificationService`, `PeerInboxProcessor`, …);
- `OdinContextUpgrades.*` patches extra permission keys or drive rights into a cloned context
  (`UsePermissions`, `UpgradeToByPassAclCheck`, `UpgradeToPeerTransferContext`), used by contacts, profile, feed,
  Shamir and the connection services;
- a `System` caller passes every file ACL.

## 10. Things noticed while writing this (not fixed)

- `IsReadonly` is not enforced anywhere **(V)**.
- On anonymous drives the comment file system skips the `Comment` check **(V)**; the impact is **(?)**.
- `CommentFileStorageService.CanWriteToDrive` checks `Write` while `AssertCanWriteToDrive` checks `Comment` **(V)**.
- `BuiltinApps` says "Nothing reads this yet" while `BuiltinProvisioner` walks it: one of the two is stale **(V)**.
