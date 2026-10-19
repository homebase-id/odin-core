# Homebase security model

A map of who may do what, and how keys back it up. Read it before touching any permission check.

Markers: **(V)** verified in the code (file named), **(?)** intent the code doesn't state. Ask the owner of
the design before acting on a (?).

Deeper docs: [app-circle-membership-plan.md](app-circle-membership-plan.md) (key model, deposits),
[app-circle-membership-status.md](app-circle-membership-status.md), [drive-addressing.md](drive-addressing.md)
(slugs, drive public key, app-owned circles), [drive-deletion-and-app-uninstall.md](drive-deletion-and-app-uninstall.md),
[connection-defaults.md](connection-defaults.md), [autoconnected-security-level-audit.md](autoconnected-security-level-audit.md),
[youauth-unified-authorization.md](youauth-unified-authorization.md), [weak-key-retirement.md](weak-key-retirement.md).

## 1. The platform

- Homebase is a generic self-sovereign **app platform**. One identity = one domain = one tenant, with its own server.
- Apps authenticate with **YouAuth**, an OAuth2-like flow: the owner consents to the drives and permission keys the app
  asks for, and each device gets a client token.
- An app may own **drives** and **circles**. Every drive has an owning `AppId`; owner drives belong to the owner console
  (`StorageDriveData.AppId`) **(V)**. Uninstalling an app can delete its drives and circles (drive-deletion doc) **(V)**.
- An app sees only what it was granted. The rule is "an app gains nothing beyond its own reach", and it is cryptographic,
  not just a check: no storage key in hand, no working grant (`PermissionContextStorageKeySource`) **(V)**.
- Each drive is encrypted with its own **storage key** (section 6).

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

## 2. Circles

- A circle is a named set of identities plus what membership grants: `DriveGrants` and `Permissions`
  (`CircleDefinition.cs`) **(V)**.
- A connected caller's rights are the union of their enabled circles' grants. A disabled circle grants nothing **(V)**.
- `AppId` on a circle: the owning app, null = owner circle. An app may create, change and delete only its own circles **(V)**.
- **Read grants need the Peer Key** (the drive's storage key is wrapped for the member). Write/react grants carry no key
  material (`RequiresPeerKey`) **(V)**. So an app *deposits* a read grant (`DepositedGrant`), and it converts to a real
  grant next time the Peer Key is in scope **(V)**.
- Hazard: an app must not plant a circle naming a drive it can't read; the owner's next grant would make it work
  ("confused deputy", drive-addressing doc). Validate at definition time **(V doc; enforcement not re-checked)**.
- Built-in circles: Chat, Friends, Email, Feed, HomePage, Emergency Location Access, Mail, Recovery, Community,
  Lists (`BuiltinCircles.cs`) **(V)**.
- Unreviewed connections are treated as `Authenticated`, not `Connected`, when content is evaluated
  (`ReviewedSecurityTier`) **(V)**.

## 3. Drives

### Drive permissions (`DrivePermission.cs`) **(V)**

| Bit | Meaning |
|---|---|
| `Read` 1 | read files |
| `Write` 2 | write files, **without** read. Deliberate: in chat you can receive, nobody can read |
| `React` 4 | add reactions |
| `Comment` 8 | write comments |
| `ConditionalTemporalRead` 16 | read only recently modified files, only via the temporal API; not part of `ReadWrite`/`All` |

- **Write-only is cryptographic**: a writer never holds the storage key **(V, plan doc)**.

### Drive options (`StorageDriveDetails`) **(V)**

| Option | Effect |
|---|---|
| `OwnerOnly` | every file must carry the Owner ACL (`FileSystemStreamWriterBase`, `FileSystemUpdateWriterBase`); can't be combined with anonymous reads or subscriptions (`DriveManager`) |
| `AllowAnonymousReads` | anyone may read files whose ACL allows it; also relaxes the comment checks (section 5) |
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

## 4. Files and their ACL

- Each file carries an `AccessControlList`: `RequiredSecurityGroup` + optional `CircleIdList` **(V)**.
- Security groups are a ladder: `Anonymous` 111 < `Authenticated` 444 < `AutoConnected` 555 < `Connected` 777 <
  `Owner` 999. A caller passes every rung at or below their own (`DriveAclAuthorizationService.Allows`) **(V)**.
- A circle list narrows further: the caller must be in at least one listed circle **(V)**.
- `OdinIdList` is not supported and admits no one **(V)**. `System` (1) is a caller level, never a file ACL **(V)**.
- Owner and `System` callers skip the ACL **(V)**.
- Reading a file needs **both** drive Read and the ACL. Queries also drop encrypted files the caller has no storage key for
  (`DriveQueryServiceBase` filter) **(V)**.
- `LocalAppMetadata` never leaves the identity.

## 5. Comments, reactions and other special file kinds

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

## 6. Envelope encryption

```
master key ──► storage key (one per drive; MasterKeyEncryptedStorageKey)
App Key / Peer Key ──► storage key (one wrapping per grant)
storage key ──► KeyHeader (per file: random AES key + IV; stored as EncryptedKeyHeader)
KeyHeader ──► payloads, thumbnails, appData.content (AES)
```

- On upload the client sends the KeyHeader wrapped in its **shared secret**; the server rewraps it with the storage key
  (`DriveStorageServiceBase`, `EncryptKeyHeaderAes`) **(V)**. Reads return it wrapped in the caller's shared secret.
- **Transit:** the sender wraps the KeyHeader with the shared secret of the CAT the recipient issued
  (`CreateTransferInstructionSet`); the recipient unwraps it from the ICR (`PeerInboxProcessor.DecryptedKeyHeaderAsync`)
  and rewraps it with its own storage key **(V)**. Payload bytes are not re-encrypted **(inferred)**.
- `IsEncrypted = false` files use an empty KeyHeader and are stored in clear **(V)**.
- `StorageDrive.AssertValidStorageKey` checks a key against the drive's encrypted id before use **(V)**.
- Ciphers: AES-CBC in `KeyHeader`, AES-GCM preferred for newer paths (CLAUDE.md, `youauth` docs).
- Write-without-read keys: drive `WriteOnlyKeyPair` (data deposits) and `PeerKeyStore.WriteOnlyKeyPair` (grant deposits)
  **(V)**.

## 7. Built-in system apps may bypass the regular checks

Shamir recovery, introductions, connections (requests, verification) and contacts are **system APIs**. They do what
they must regardless of the caller's ordinary permissions. Don't "fix" them to use the file rule.

How they bypass **(V)**:
- `GetIcrAsync(..., overrideHack: true)` reads a connection without `ReadConnections`
  (`CircleNetworkIntroductionService`, `CircleNetworkService`, `CircleNetworkRequestService`, `ShamirRecoveryService`,
  `CircleNetworkVerificationService`, `PeerInboxProcessor`, …);
- `OdinContextUpgrades.*` patches extra permission keys or drive rights into a cloned context
  (`UsePermissions`, `UpgradeToByPassAclCheck`, `UpgradeToPeerTransferContext`), used by contacts, profile, feed,
  Shamir and the connection services;
- a `System` caller passes every file ACL.

Built-in apps (`SystemAppConstants`) can't be uninstalled (`AppUninstallService.IsBuiltIn`) **(V)**.

## 8. Things noticed while writing this (not fixed)

- `IsReadonly` is not enforced anywhere **(V)**.
- On anonymous drives the comment file system skips the `Comment` check **(V)**; the impact is **(?)**.
- `CommentFileStorageService.CanWriteToDrive` checks `Write` while `AssertCanWriteToDrive` checks `Comment` **(V)**.
