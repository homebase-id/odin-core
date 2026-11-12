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
- Apps own **drives** and **circles** (section 2). Each drive is encrypted with its own **drive key**, which the code
  and this doc call the storage key (section 8).
- An app sees only what it was granted: "an app gains nothing beyond its own reach". This is cryptographic, not just a
  check: no storage key in hand, no working grant (`PermissionContextStorageKeySource`) **(V)**.
- Words as in [terminology.md](terminology.md). A drive's **sharing model** says who holds its key (section 3).
- **Server Share drives are zero knowledge at rest:** the server stores the drive key locked, and opens it only in
  memory while the owner, one of the owner's apps or a member is online and calling the API **(not traced for every
  key path, inferred)**.
- **Zero-knowledge drives** (sharing model Private or Private Share: a new identity's Vault and Email from the start
  (`BuiltinDrives`), an older identity's Vault, Email and Filesync and every identity's Shard Recovery once the
  browser converts them (`ZkDriveConversionService.ConvertibleDrives`), and apps' own, such as Filesync's Private Share
  drive) are zero knowledge: the server never holds a key that opens them, not even in memory. They have no master-key
  copy of their key: the only copy is locked by the owner's zero-knowledge master key, which only the browser opens
  (`StorageDrive.IsZeroKnowledge`). The server never encrypts for one, and no grant on one carries its key
  (`StorageDrive.GrantCarriesStorageKey`) **(V)**. One definition answers "is it zero knowledge": the model
  (`StorageDrive.IsZeroKnowledge` asks `DriveSharingModel.IsZeroKnowledgeDrive()`), and the key copies follow from it:
  a save refuses a zero-knowledge model with a master-key copy, a Server Share model without one, and a drive with
  neither (`DriveManager.AssertKeyCopies`) **(V)**. Creation sets the model, and so does conversion
  (`DriveManager.ApplyZeroKnowledgeKeysAsync`) **(V)**.
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
| Guest / home-site visitor | client token (`BuiltInBrowserApp`, `XT32` cookie) | no grant of its own: what its connection (or follow) already allows (`HomeAuthenticatorService`) |
| Peer (another identity) | TLS client cert + CAT | CAT → **Peer Key** → that connection's circle grants |
| Peer's device (direct read) | the peer-notification token its own server fetched for the peer socket (`PeerNotificationSubscriber`) | no key: Read on the zero-knowledge drives that connection's circle grants reach (app circles included), nothing else, and only on the drive reads marked `[PeerDirectRead]` (header, payload, thumbnail, query) of such a drive; anywhere else the caller is anonymous (`CircleNetworkService.TryCreatePeerSubscriberReadContextAsync`, `PeerSubscriberAuthPathHandler`) **(V, tested)**. A client-locked file goes out as stored; an unencrypted one and every file's metadata as on the relay |
| Anonymous | nothing | drives with `AllowAnonymousReads` only |

- The App Key and Peer Key are hub keys: each grant beneath them is wrapped once, individually (plan doc) **(V)**.
- An app gets the ICR key only when it requests a transit permission; guests have none **(V)**, from the #1977 work.
- Both kinds of guest pass the same checks: V1 gives them the same claims (`YouAuthAuthenticationHandler`), and V2's
  guest policies take both token types (`UnifiedPolicies.GuestTokenTypes`) **(V)**.

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

### Drive options (`StorageDriveDetails`) **(V)**

| Option | Effect |
|---|---|
| `OwnerOnly` | every file must carry the Owner ACL (`FileSystemStreamWriterBase`, `FileSystemUpdateWriterBase`); can't be combined with anonymous reads or subscriptions (`DriveManager`) |
| `AllowAnonymousReads` | anyone may read files whose ACL allows it; also relaxes the comment checks (section 6) |
| `AllowSubscriptions` | followers may subscribe (feed); a drive marked as a community channel needs it |
| `AllowCdn` | the CDN may read payloads (`IsCdnEnabled`); anonymous drives are reachable anyway |
| `RequirePayloadHashes` | every payload must carry client hashes |
| `BlockEnumeration` | only the owner, the owner's apps and the server may list or count its files (every query, the drive size; a collection's section answers as a drive the caller can't read) (`DriveQueryServiceBase.DriveToListAsync`, `StorageDrive.MayBeListedBy`), or hear of them on a socket (`DriveEventMessage.VisibleToAsync`); anyone else reaches a file only by an address they were given, a WebDrop link's unique id. For anyone but the owner, its apps and the server, `expire-now` works only on such a drive (`HastenExpiryAsync`). Can't be combined with `AllowSubscriptions`. Absent = listable. A system drive has what its built-in definition says, whatever is stored (`BuiltinDrives.BlockEnumerationOf`): WebDrop's blocks it **(V, tested)** |
| `IsReadonly` | documented "owner only, in owner auth". **No write path reads it** (grep) **(V)**, so it's not enforced |
| `IsArchived` | non-owner callers are refused (`AssertDriveIsNotArchived`) |
| `WriteOnlyKeyPair` | per-drive ECC-384 key: seal a deposit without being able to read; private half under the storage key. Its public half is served to any identity, with whether the asker may write (`CallerMayWrite`); a deposit still needs Write |
| Attribute `IsCollaborativeChannel` | marks the drive as a **community channel** (`IsCollaborationDrive`): members may change each other's posts, and posts keep the sender's ACL ([file-access.md](file-access.md#drives-marked-as-a-community-channel)) |

- System drives (`BuiltinDrives.Protected`) can't be deleted **(V)**.
- **A zero-knowledge drive allows neither anonymous reads, the CDN nor subscriptions,** and may not be marked as a
  community channel, which needs the server to hold the key; a Private Share drive can't be `OwnerOnly` either, which
  would leave it nothing to share. One rule (`DriveSettings`), by the drive's model: every setting at
  creation (`CreateDriveRequest.AssertSharingModel`, `DriveSettings.AssertFit`); a conversion
  (`DriveManager.ApplyZeroKnowledgeKeysAsync`) turns anonymous reads, the CDN and subscriptions off and refuses the
  rest (`DriveSettings.FitForConversion`), so a re-key after recovery never fails on them; an attribute change (`UpdateAttributesAsync`) and the read-mode, subscriptions and CDN
  setters (`SetDriveReadModeAsync`, `SetDriveAllowSubscriptionsAsync`, `SetDriveAllowCdnAsync`) ask only the setting
  they change, as it will be (`DriveSettings.AssertChangeFits`), so turning a setting off always works and a drive
  with a setting set before the rule can be brought into line **(V, tested)**.
- Tenant settings add rights on anonymous drives for authenticated and connected identities: Read always,
  Comment/React by flag (`TenantSettings.GetAnonymousDrivePermissionsFor…`) **(V)**.

### Sharing models (`Drives.SharingModel`) **(V)**

| Model | Who holds the drive key |
|---|---|
| **Server Share** (`Collaborative` in code; the default) | the server, locked at rest; it opens it for the owner, the owner's apps and the members of the drive's circles, whose grants carry it |
| **Private** | the owner's devices and apps only; others may only add deposits |
| **Private Share** (`SelectiveShare` in code) | the owner's devices and apps only; others add deposits, and read what is shared with them through shares ([file-access.md](file-access.md#with-private-share)) |

An app chooses the model when it creates the drive; an existing drive changes it only by conversion **(V)**.

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
  to any identity, on every drive; a missing drive is a 404. The key is public: sealing to it puts nothing on the
  drive, and a deposit still needs Write when it arrives, so a stranger with no grant can't deposit yet
  ([weak-key-retirement.md](weak-key-retirement.md)). Sharers need it to seal a key for the owner to open, such as a
  Private Share circle key. The answer also says the drive exists, whether it is zero knowledge, its sharing
  model, and whether the caller may write to it (`CallerMayWrite`), which senders need: Shamir dealing refuses a
  holder who doesn't let the dealer write, rather than deal a shard their server will refuse. So any identity can
  list an identity's apps and drives by slug through it; address resolution still answers only a caller with a
  grant, as it also gives the drive's id and type.
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
  anyone into an owner circle **(V)**. An app creates `Personal` and `Individual` circles, enrolling by hand or on
  connect, the latter only with `ReadConnections`, since the app may list its own circle's members
  (`CreateAppCircleRequest.AssertKind`) **(V)**.
- **Who may see a circle's members** (and who is pending in it): the owner; the app that owns the circle, always; any
  other app only with `ReadCircleMembership` (`CircleMembershipService.AssertCallerMayReadMembersAsync`) **(V)**.
  Listing every circle with its members still needs the key.
- **Read grants need the Peer Key** (the drive's storage key is wrapped for the member). Write/react grants, and every
  grant on a zero-knowledge drive, carry no key material (`CircleNetworkService.RequiresPeerKeyAsync`, which asks
  `StorageDrive.GrantCarriesStorageKey`) **(V)**. So an app *deposits* a read grant (`DepositedGrant`), and it converts to a real
  grant next time the Peer Key is in scope **(V)**.
- **No confused deputy:** an app can't plant a circle naming a drive it can't read, which the owner's next grant would
  otherwise make work ([archive/drive-addressing.md](archive/drive-addressing.md#circles)). Creation is bounded by what the app holds (section 2), and updating a
  circle definition needs the master key (`CircleMembershipService.UpdateAsync`) **(V)**.
- Unreviewed connections are treated as `Authenticated`, not `Connected`, when content is evaluated
  (`ReviewedSecurityTier`) **(V)**.
- **Circle share keys** (Private Share, [selective-share-plan.md](selective-share-plan.md)): an app circle's key,
  locked with the owning app's zero-knowledge key, and each member's copy, sealed to their matching drive. The server
  opens neither, and checks only their size and format version (`CircleShareKeyService`) **(V)**.
  - Only the owning app or the owner console may read or set the key, list members without a copy, or store copies
    (`CircleMembershipService.GetCircleCallerMayManageAsync`); never on an owner circle. No permission key: owning the
    circle is the authority, as for enabling or disabling it **(V)**. An app is refused alike for a missing circle,
    an owner circle and another app's circle; only the owner console is told a circle doesn't exist **(V)**.
  - The key is set once; a member's copy may be replaced. A copy is stored only for a member, and only while the circle
    holds the key the app says it sealed from (`SealedFrom`), checked again in the same statement, so a reset
    meanwhile can't leave copies of the old key (`CircleShareKeyChanged`) **(V)**. The check locks the circle's row,
    and the reset clears the key before the copies (`TableCircleMember.SetSealedShareKeysAsync`) **(V; on Postgres a
    test overlaps a seal and a reset in both orders)**. The one way back is a developer's reset, owner console only: it clears the key and every
    copy (`CircleShareKeyService.ResetShareKeyAsync`) **(V)**.
  - A member fetches only their own copy, over the Peer API, while the circle is one of their enabled circles and only
    once the owner has reviewed the connection (`ReviewedSecurityTier`: the circle key opens content, which an
    unreviewed connection may not see); a copy sealed before the review is served after it. Before the review the
    refusal carries `NotReviewedByOwner`, which the member's server passes on, so their app can say so. The row is looked up by
    the caller's identity, never by anything they send **(V)**.
  - On the member's side, any app with `UseTransitRead` may fetch this identity's copy from a peer
    (`PeerCircleShareKeyService`), whichever app owns that circle **(V)**. By design: the copy is sealed to the
    member's matching drive, so only an app holding that drive's key can open it (the plan; the server can't check
    what a blob is sealed to). What another app learns is which peers' circles grant this identity **(I)**.
  - Rewriting a circle or a membership row keeps both: the generated upsert and update leave the two columns out
    (`keepOnUpsert`), and saving a connection reconciles the member's rows instead of deleting and re-inserting them
    (`CircleMembershipService.ReconcileMemberCirclesAsync`). Leaving the circle or disconnecting deletes the member's
    row and with it their copy **(V)**.

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

The full description, today's and Private Share's, is [file-access.md](file-access.md).

- Each file carries an `AccessControlList`: `RequiredSecurityGroup` + optional `CircleIdList` and `OdinIdList` **(V)**.
- Security groups are a ladder: `Anonymous` 111 < `Authenticated` 444 < `AutoConnected` 555 < `Connected` 777 <
  `Owner` 999. A caller passes every rung at or below their own (`DriveAclAuthorizationService.Allows`) **(V)**.
  A group that is missing or undefined (0, or any other number that names no rung) means `Owner` (`AccessControlList`) **(V)**.
- Named circles and named identities narrow further, together: the caller must be in at least one listed circle **or**
  be one of the listed identities, and still meet the security group, so a named identity that is unconnected or
  unreviewed does not pass a `Connected` ACL **(V)**. Queries apply the same rule in SQL (`aclAnyOf`) **(V)**.
- `OdinIdList` is stored canonical and de-duplicated by `AccessControlList.Validate`, and is cleared before a file is
  sent to a peer, so a recipient never learns who else was named **(V)**. `System` (1) is a caller level, never a file
  ACL **(V)**.
- Owner and `System` callers skip the ACL **(V)**.
- Reading a file needs **both** drive Read and the ACL. Past drive Read, one rule decides whether the caller may see a stored
  file (`IDriveAclAuthorizationService.CallerMayReadAsync`): the ACL, and a client-locked file (sealed, zkDrive or fileKey: `EncryptedKeyHeader.IsLockedByClient`) only for a caller holding the
  drive's key, except a deposit staged on the transient drive, and a deposit on a Server Share drive, which reaches a caller
  holding that drive's storage key since the server opens it (`ZkDriveFiles.VisibleTo`); on a Private Share drive an
  outside caller's live share decides instead, and opens a client-locked file to them too
  (`DriveAclAuthorizationService.CallerMayReadAsync`) **(V)**. A read by id, a query and a socket event all ask it **(V)**. Queries also drop
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
    (`AssertCanTellRecipientsToDelete`, #1978) **(V)**. Why comments are looser: **(?)**;
  - **who may comment follows the sharing model:** on a Server Share drive the members who may read, by the checks
    above; on a Private Share drive through shares (below); on a Private drive only owner callers (and the server's own jobs, a
    comment's TTL): any other caller's comment, new, an update or a delete, is refused, a local upload (a guest or YouAuth caller) as well as a peer's
    (`CommentFileStorageService.AssertCanWriteToDrive` / `CanWriteToDrive`) **(V, tested for a peer who may read the
    file and holds `Comment`, against the same comment landing on a Server Share drive:
    `PrivateDrivePeerWritesTests.OnlyOwnerCallersCommentOnAPrivateDrive`)**.
- **Reactions** need `React` on the drive (`ReactionContentService`, `GroupReactionService`) **(V)**. Their peer rules are
  their own; don't fold them into the file rule (user decision, #1977). They stay allowed on Private drives: Chat
  needs them. On a zero-knowledge drive a peer's reaction is applied at once, the file, a standard file or a comment,
  found in the request's file system without the read rule, asking drive `React` rather than `Read` or `Write`
  (`PeerIncomingGroupReactionInboxRouterService.TryApplyToZeroKnowledgeDriveAsync`,
  `GetServerFileHeaderByGlobalTransitIdSkippingFileAcl(…, permission)`) **(V, tested; I: this group-reaction route has
  no test of its own for a peer holding `React` alone)**.
- **Read receipts** (transfer history): own rules, hands off (user decision).
- **On a Private Share drive** an outside caller's comments and reactions need drive `Comment` or `React` as
  anywhere, and a live share row on the file with the same bit: a share row narrows its circles' grant, for every bit
  ([file-access.md](file-access.md#a-share-row-narrows-its-circles-grant)); a comment is read by whoever may read its file, is encrypted
  with its file's key (`fileKey`), and is written only by its author ([file-access.md](file-access.md#with-private-share),
  selective-share-pr3-plan.md section 10).

### Peer file operations (#1977)

- Plain file ops on a peer: get/query = `UseTransitRead`; send/update/delete = `UseTransitWrite`
  (`PermissionContext.AssertCanUsePeer(PeerAccess)`) **(V)**. Asking whether one's own send from the transient drive
  is still waiting in the outbox (`send-status`) is part of sending: `UseTransitWrite` **(V)**.
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
  - **`reactionPreview`** (`UpdateReactionSummary`; a reaction added or deleted changes it in one read inside storage,
    `ChangeReactionSummary`, from all three `ReactionPreviewCalculator` reaction handlers) doesn't advance the file's `versionTag`. A header write carries the
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
- **Server Share drive:** by the server with the drive's key (DrvK, `type: aes`). A peer's key header arrives
  wrapped in the connection's shared secret and waits in the drive's inbox until the owner or an app is
  signed in; the server then re-locks it with the keys that caller lends it (`PeerInboxProcessor`) **(V)**. Or the
  peer seals it to the drive's write-only public key (a deposit): it is written at once, and the server opens it and
  locks it as its own on a read whose request carries DrvK, before the read rule
  ([zero-knowledge-drives.md](zero-knowledge-drives.md), "Server Share") **(V, tested)**.
  Only a Server Share drive needs an inbox for a peer's files, since only there does the server re-lock what arrives;
  the plan is to remove it there too, later. Read receipts go through the inbox on every drive
  (`PeerDriveIncomingTransferService.MarkFileAsReadAsync`) **(V)**.
- **Zero-knowledge drive:** by a client, and the server opens neither kind: by a device holding DrvK
  (`type: zkDrive`), or sealed to the drive's write-only public key by anyone with Write (`type: sealed`,
  a deposit) **(V)**. A caller holding DrvK may transform `sealed` into `zkDrive`, keeping the file's version tag
  and `modified`; never on the Shard Recovery drive ([zero-knowledge-drives.md](zero-knowledge-drives.md))
  **(V, tested)**. One exception writes server-side: on a Private Email drive the server seals the OpenPGP keyring and its
  pointer to the drive itself (`EmailKeyMaterialWriter`), skipping the write rules a client's upload passes
  (`OpenForWrite`, `AssertMayReplace`), so a rotation's sealed pointer replaces a `zkDrive` one on an upgraded drive;
  it refuses a caller that doesn't hold the drive's key **(V)**.
  - **The inbox:** none on a zero-knowledge drive (`StorageDrive.HasInbox`) for a peer's files, updates, deletes and
    reactions; read receipts still go through it. One check decides a direct write for a peer's new files and updates
    alike (`PeerDirectWrites.CanWriteAtOnce`), made once on arrival in both incoming services
    (`PeerDriveIncomingTransferService`, `PeerDriveIncomingFileUpdateService`) and reused when the transfer finishes: at once unless the file is
    encrypted, on a Server Share drive, with a key header the server must re-lock, and the caller lacks the drive's key.
    A key header passed on as sent (`sealed`, `share`, `fileKey`: `EncryptedKeyHeader.IsPassedOnAsSent`) needs no key,
    and a comment that can't be written at once is refused, never queued **(V)**. A peer's delete of a standard file
    and a peer's reaction on a zero-knowledge drive are written at once too, the file found without the read rule
    (`AcceptDeleteLinkedFileRequestAsync`, `PeerIncomingGroupReactionInboxRouterService`) **(V, tested)**; who may
    delete is the rule for changing a file you sent ([file-access.md](file-access.md#changing-a-file-you-sent)), with
    the delete's own limits there (`SentFileChanges.ContextForDeleteAsync`) **(V)**.
  - One read rule (`IDriveAclAuthorizationService.CallerMayReadAsync`) shows a client-locked file only to a caller
    that can open it; the model and decisions are in
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
- A zero-knowledge drive takes only key headers an owner device locked with its key (`zkDrive`),
  deposits, and on a Private Share drive comments locked with their file's key (`fileKey`)
  (`ZkDriveFiles.AssertDriveTakes`) **(V)**. It refuses one locked with the shared secret by its type, before opening it
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
