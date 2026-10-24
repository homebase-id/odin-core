# Zero-knowledge drives

**Status:** direction only, nothing built. This is the starting point. Details get settled as each
piece is designed.

Markers follow [security-model.md](security-model.md): **(V)** verified in the code (file named).
Anything without a marker is the proposed design.

## Goal

- **Today:** files are encrypted at rest, but the server briefly holds the keys in memory while
  the user calls the API.
- **Goal:** for private and selective share drives, the server shall never sees a drive key or a file key, even briefly. 
  Keys are opened only on the user's own devices. Collaborative drives will keep working like today.
- **Also:** a peer you share with never gets your drive key.
- **Scope:** this applies to the Private and Selective Share drives below. Collaborative drives keep
  today's model.

## Where the server sees keys today

- **Upload:** the client sends the KeyHeader wrapped in its shared secret, and the server
  rewraps it with the storage key (`DriveStorageServiceBase.cs`, `EncryptKeyHeaderAes`) **(V)**.
- **Read:** the server unwraps the KeyHeader with the storage key and rewraps it for the caller
  (`DriveFileUtility.CreateClientFileHeader`) **(V)**.
- **Peer transfer:** the server rewraps the KeyHeader once per recipient
  (`PeerOutgoingTransferService.cs`) **(V)**. The recipient's server unwraps it and rewraps it
  with its own storage key (`PeerInboxProcessor`) **(V)**.
- **Circle grants:** a circle's drive grant carries the drive storage key, wrapped under the
  Peer Key (`DriveGrant.KeyStoreKeyEncryptedStorageKey`) **(V)**. So a connected peer's
  credentials unlock the whole drive key on our server.

## Three sharing models

Every drive picks one sharing model when it is created. All three are encrypted at rest.
Private and Selective Share are fully zero knowledge: no key is ever on the server, not even in
memory. Collaborative works exactly as today, with in-memory blips.

| Sharing model | In one line | Example |
|---|---|---|
| **Private** | Zero knowledge, nothing shared. Others can only deposit. | Vault, chat |
| **Selective Share** | Zero knowledge, plus an extra key per circle or per person | Filesync: share one folder |
| **Collaborative** (as today) | Encrypted at rest, with in-memory blips, exactly as today. No per-file client work, however many files | Community, secure feed channel |

Naming notes:
- "Type" is taken by `DriveType` / `TargetDrive.Type`.
- "Key model" is already used for key vocabulary in
  [app-circle-membership-plan.md](app-circle-membership-plan.md).
- `OwnerOnly` is an existing flag.

### Private

- **For:** data nobody else reads (Vault), and inboxes others only write to (chat).
- **Who holds the drive key:** the owner's devices and apps only.
- **Others:** deposit by locking to the drive's public key (`WriteOnlyKeyPair`, private half
  escrowed under the storage key, `StorageDrive.cs`) **(V)**. They can write but never read.
- **Chat:** the sending client locks each message key to every recipient's chat drive public key,
  all in the one upload. The recipient's server stores the result as-is.

### Selective Share

- **For:** a private drive where you share chosen files or folders. Example: sharing one filesync
  folder with John.
- **Who holds the drive key:** the owner only, as with Private.
- **Share key:** each circle (or a one-off share with one or more persons) has its own key, kept under the
  App Key of the app that owns the circle.
- **Members:** each member gets the share key, locked to the public key of their matching drive.
  That's one row per member.
- **Files:** each shared file gets one extra header per share, locked with the share key. It's
  stored in a side table next to the file, modelled on `DriveAclIndex` **(V)**.
- **Cost:** sharing new files is free, because the header rides in the upload. Sharing many
  existing files is a client bulk job.

### Collaborative

- **For:** spaces where every member reads basically everything, such as a community or a secure feed
  channel. Things on the master that members cannot read are protected by ACL, not encryption.
- **Works exactly as today.** Encrypted at rest. The server briefly unlocks the drive key in
  memory to rewrap headers for members and peers.
- **Why not fully zero knowledge:** a big community drive can easily hold a million files. Any
  scheme where clients write keys per file would in some scenarios require clients update 
  very large volume of file records over the network. Today's model needs no client work at all.
- **Writing:** the server enforces who may write, as today.

## Direction: how we get there

For Private and Selective Share drives:

- **A second master key, the zero-knowledge master key.**
  - Today's master key stays as it is, and keeps serving Collaborative drives and the server-side
    features.
  - The new one is random and stored on the server locked by a second password key. It is unlocked
    only in the browser.
  - The second password key is pbkdf2(password, second salt): the same algorithm as today's KEK
    (`PasswordDataManager.cs`) **(V)**. It is made from the password itself, never from the KEK,
    because the KEK is sent to the server today **(V)**.
  - Password change works as today. The browser also re-locks the new master key.
- **ZK Drive keys are random and stored**, locked by the zero-knowledge master key (see
  [Schema additions](#schema-additions)).
- **ZK Keys open on devices only.**
  - Apps and devices get their keys sealed to their own ECC P-384 public key, in the same
    hand-off as today's app sign-in (see [Getting drive keys to an app](#getting-drive-keys-to-an-app)).
- **Lock-to-public-key is the one primitive.** Plain ECDH P-384 plus AES, the shape of today's
  `EccEncryptedPayload` (throwaway key pair + hint) **(V)**.
- **The receive key is the receiving drive's public key**, not a new identity or app key. Drives
  belong to apps, so apps stay isolated from each other.
- **Peers' servers only pass locked data through** (see [Peers](#peers)).
- **Recovery** comes first (see [Order of work](#order-of-work)).

## Order of work

Recovery comes before any drive goes zero knowledge, so nobody ever has a zero-knowledge drive
without a way back.

Plans for steps 1 and 2, in two halves: A, keys and database, is the current plan
([zero-knowledge-keys-plan.md](zero-knowledge-keys-plan.md)). B, Shamir in the browser, follows
([zero-knowledge-recovery-plan.md](zero-knowledge-recovery-plan.md)).

1. **Database and browser keys.**
   - The new password data and drive columns (see [Schema additions](#schema-additions)). They
     can land before anything uses them.
   - At login the browser works out the second password key. The first time, it creates the ZK
     master key and saves it locked. Password change locks it again.
   - Owner-only endpoints to read and save the two password fields.
   - The Shard Recovery drive converts to zero knowledge. Its Shamir files aren't encrypted with
     the drive key (the shards inside are locked with the dealer's own keys), so the browser
     simply replaces its drive key and write-only key pair.
2. **Shamir in the browser** (see [Recovery](#recovery)).
   - At login the browser also works out the login keys, deals the shards, and locks each to its
     holder's Shard Recovery drive public key.
   - Holders' keys are reachable today: peers have Write on that drive through a built-in circle
     and an automatic write grant (`BuiltinCircles.cs`, `CircleNetworkService.cs`) **(V)**.
   - Existing Shamir setups are dealt again by the browser, once.
3. **Recovery phrase,** the same way. Small once Shamir works.
4. **Private drives and conversion,** Vault first (see
   [Private drives and conversion](#private-drives-and-conversion)).
   - Every drive gets a ZK copy of its key, whatever its sharing model, and every new drive
     always gets one from then on (see [Every drive gets a ZK copy](#every-drive-gets-a-zk-copy)).
   - Consent seals the keys of every drive an app is granted to the app.
5. **Chat,** once deposits exist (see [Work for Chat and Vault](#work-for-chat-and-vault)).

## Peers

The goal for both zero-knowledge models: no server opens anything for a peer read or a deposit.
Keys open on the peer's devices.

### Today **(V)**

- **Peer read, on your server:** the incoming peer query (`PeerDriveQueryService`, incoming) builds
  the header with `DriveFileUtility.CreateClientFileHeader`. That unwraps the KeyHeader with the
  storage key from the circle's drive grant and rewraps it with the connection's shared secret.
- **Peer read, on the reader's server:** `PeerDriveQueryService.TransformSharedSecret` (outgoing)
  unwraps it with the connection's shared secret and rewraps it for the reader's app.
- **Incoming transfer:** the recipient's server rewraps with its own storage key
  (`PeerInboxProcessor`).

### Private

- **Reads:** the Peer API does no encryption or decryption for any read. A Private drive has no
  peer readers, so no circle's drive grant carries its storage key.
- **Deposits:** the sending client locks to the drive's public key. The sending server, the
  receiving server and the inbox store it as received.

### Selective Share

- **Reads:** the Peer API returns the extra header as stored, locked with the share key. Neither
  server opens it.
- **On the peer's device:** open the Peers row (the share key, locked to the peer's drive public
  key), then the extra header, then the file.
- **No storage key in the grant:** the circle's drive grant for a Selective Share drive carries no
  storage key (`KeyStoreKeyEncryptedStorageKey` stays empty).

### Anonymous and temporal reads

Both can make the server open files for the reader.
- **Anonymous reads** (`AllowAnonymousReads`): an anonymous reader has no keys.
  - An unencrypted file needs no key: the server returns an empty key header
    (`DriveFileUtility.CreateClientFileHeader`) **(V)**. Serving it opens nothing.
  - **Private:** no anonymous reads. Today an `OwnerOnly` drive already can't allow them
    (`DriveManager.cs`, `CannotAllowAnonymousReadsOnOwnerOnlyDrive`) **(V)**.
  - **Selective Share:** anonymous reads are for unencrypted files. An anonymous reader who gets
    hold of an encrypted file gets nothing useful: it is locked, and the server has no key to open
    it. Examples: Homepage Config, Profile, WebDrop.
- **Temporal reads** (`DrivePermission.ConditionalTemporalRead`, a circle may read files changed
  in a recent window): the grant escrows the drive storage key so the server can rewrap for the
  reader (`ExchangeGrantService.cs`) **(V)**. Proposed: not allowed on Private or Selective Share.
  On Selective Share, share the recent files instead.

## Schema additions

Agreed so far. Names may still change. SQLGenerator for anything SQL.

- **Password data** (JSON in the KeyValue table, `PasswordData`, `OwnerSecretService.cs`) **(V)**:
  - `ZkSaltKek`, new: the second salt. The browser works out the second password key as
    pbkdf2(password, `ZkSaltKek`), with the same algorithm and iterations as `SaltKek`.
  - `ZkKekEncryptedZkMasterKey`, new: the zero-knowledge master key, locked by the second password
    key. A `SymmetricKeyEncryptedAes` carries its own IV **(V)**, so no extra IV is stored.
- **Drives** (`TableDrivesCRUD.cs`) **(V)**:
  - `ZkMasterKeyEncryptedStorageKey`, new: the drive storage key, locked by the zero-knowledge
    master key. Only the browser can open it. Its data type follows the SQL generator.
  - `SharingModel`, new: Private, Selective Share or Collaborative.
  - `MasterKeyEncryptedStorageKeyJson`, existing and `NOT NULL` today **(V)**: on a
    zero-knowledge drive it must not hold a copy the server can open. Nullable, or a placeholder.
  - `WriteOnlyKeyPair`, existing **(V)**: its private half is locked by the storage key, so on a
    zero-knowledge drive the browser creates it. Today the server does
    (`DriveManager.EnsureWriteOnlyKeyPairAsync`) **(V)**.
- **New drives:** an app opens the owner console to create a drive, as it does today. The
  browser makes the storage key and the key pair there.

### Migration: setting the sharing model on existing drives

The migration that adds `SharingModel` labels nothing: every existing drive stays Collaborative,
which is today's behaviour, and so is every new drive. A drive takes its model only when the
browser converts it, so the label never claims zero knowledge the drive doesn't have, and
`DriveManager` refuses to save a Private drive that still has a master-key copy **(V)**. Shard
Recovery is the first (plan A).

- **The model each built-in drive gets when it is converted** (the drives in `BuiltinDrives.cs` **(V)**):

  | Drive | Sharing model | Why |
  |---|---|---|
  | Contacts, Email, Feed, Wallet, Vault | Private | `OwnerOnly = true` today **(V)** |
  | Chat, Mail, Shard Recovery | Private | Owner reads; others only deposit |
  | Photo Library, Sticker, Location, Lists, Moments | Selective Share | Private, with chosen shares |
  | Homepage Config, Profile, WebDrop | Selective Share | Anonymous reads are for unencrypted files |
  | Community, Public Posts | Collaborative | Members read everything |
  | Transient temp | Unclassified for now | `SharingModel` has an "unclassified" value |

- **Anonymous reads and temporal read grants limit which model a drive can take** (see
  [Anonymous and temporal reads](#anonymous-and-temporal-reads)).
- **Every other drive, including third-party app drives:** Collaborative, which is today's
  behaviour.
- **To check before a drive becomes Private:**
  - Email, Feed and Contacts may have server features that read plaintext (see [Open](#open)).
  - Chat and Mail have `OwnerOnly = false` today **(V)**, so check what reads them besides the owner.
- **Converting sets the label.** The server already knows an existing drive's storage key. A
  drive becomes zero knowledge, and gets its model, only when the browser converts it (see
  [Converting an existing drive](#converting-an-existing-drive)).

## Getting ZK drive keys to an app

Same principle and same protection as today's app sign-in (YouAuth): what the app receives is
sealed to the app's own key, so nobody in between can open it.

**Who is who:**
- **Browser:** the owner console page, running on the owner's own device. It is the only place
  the zero-knowledge master key is ever unlocked. Every zero-knowledge step below runs here,
  never on the server.
- **Server:** the identity's server. It only stores and forwards sealed data.
- **App:** the client app on one device (chat, filesync). It makes an ECC P-384 key pair for each
  sign-in.

**Today** (`YouAuthUnifiedController`, `YouAuthUnifiedService.CreateClientAccessTokenAsync`) **(V)**:
1. The app sends its public key in the authorize request (`public_key`).
2. The server makes a throwaway P-384 key pair and a 16-byte salt. ECDH(server throwaway private
   key, app public key, salt) gives the exchange secret. That secret seals the shared secret and
   the client token (`EncryptedTokenExchange`).
3. The server keeps the sealed result in the cache for 5 minutes, under a hash of the exchange
   secret.
4. The redirect back to the app carries only the server's throwaway public key and the salt. The
   browser passes them on but can't open anything with them.
5. The app works out the same exchange secret and fetches the sealed result once, by its hash.
   The server then deletes it (`ExchangeDigestForEncryptedToken`).

**New:** the same steps. The browser locks the drive keys, and the server only carries them.

Labels: **ZK** = the zero-knowledge master key, **MK** = today's master key, **PK** = locking to an
ECC P-384 public key.

Browser (owner console, consent page):
1. Take the ZK master key it has held since login (the password is asked for at login only).
   (**ZK**)
2. Open the key of each drive the app is granted, with the ZK master key. (**ZK**)
3. Lock those drive keys to the app's public key from the authorize request, which travels in
   the consent page's return URL **(V)**. (**PK**)
4. Post the locked blob with the consent to the server.

Server (YouAuth):

5. Store the blob untouched, as it holds the consent today (`TempConsentCacheKey`) **(V)**. It
   can't open it: it has neither the ZK master key nor the app's private key.
6. Put the blob in the token response as one new field, `ZkDriveKeys`, in the same 5-minute,
   one-time entry (`EncryptedTokenExchange`, `YouAuthTokenResponse`).

App:

7. Open `ZkDriveKeys` with its own private key. (**PK**)
8. Keep the drive keys in the device's key store.

**MK** isn't used here. It is used once per existing drive, at conversion: the server opens the
drive key with MK, the browser locks it with ZK, and the MK copy is deleted (see
[Converting an existing drive](#converting-an-existing-drive)).

**Who sees what:**
- **Server:** only the sealed blob. It has neither the app's private key nor the browser's
  throwaway private key.
- **Redirect, or anyone in the middle:** public keys and salts only, as today.
- **Browser:** the drive keys, while it seals them. It is the owner's own page, and it holds the
  zero-knowledge master key anyway.
- **App:** only the storage keys of the drives it was granted, never the zero-knowledge master key.

**Same protection, same limits:**
- The browser seals to the key in the authorize request, the same key the server seals the
  token to today. Anyone able to swap that key would get today's token too, so this adds no new
  gap.
- The browser runs owner console code that the server delivers. A server sending altered code
  could take keys from the page. That is a limit of any zero-knowledge design that runs in a web
  page; it is reasoning, not something checked in this repo.

**Each device, each new grant:**
- Each device signs in on its own and gets its own sealed copy.
- Granting an app more drives later goes through the consent page again, and that's where those
  keys get sealed.

**Trusted apps (no consent screen):**
- Today an app whose redirect is on the owner's own host skips consent: the server answers the
  authorize request itself and redirects straight back to the app (`YouAuthUnifiedService.NeedConsent`,
  `YouAuthUnifiedController`) **(V)**. They are short-circuited on purpose: they are apps we
  trust, and it keeps sign-in smooth.
- New: the server sends them to the consent page anyway. The page shows nothing and needs no
  click: it runs steps 1–4 and continues on its own. The user sees one extra redirect.
- It has to pass through the page because only the browser holds the ZK master key. A redirect
  from the server alone has no keys to hand over.

## Private drives and conversion

The smallest change that makes Private drives zero knowledge, and moves existing drives over
without re-encrypting any file header.

### Converting an existing drive

The existing drive key is kept and re-locked with the zero-knowledge master key. Every file
header stays locked with the same drive key, so nothing is re-encrypted.

1. The owner logs in. The browser works out the second password key from the password. On the
   first login after the change, it also creates the zero-knowledge master key and saves it
   locked.
2. The browser asks for the drive key. The server opens it with today's master key and sends it
   locked to the owner's shared secret, as reads do today. This is the server's last look at
   that key.
3. The browser locks the key with the zero-knowledge master key and sends that copy back.
4. In one step, the server stores the new copy, sets the drive to Private, and deletes every copy
   it could open:
   - the master key copy (`MasterKeyEncryptedStorageKeyJson`)
   - the copy in each app and circle grant (`DriveGrant.KeyStoreKeyEncryptedStorageKey`) **(V)**
   - cached permission contexts (`OdinContextCache`, 60 minutes) **(V)**

### What changes

Server:
1. Password data: the two new fields, and endpoints to save them (first login, password change).
2. Drives: `ZkMasterKeyEncryptedStorageKey` and `SharingModel`. The master key copy becomes
   nullable.
3. On a Private drive the server stores and returns file headers as they are, with no
   re-locking. That covers four places: building a reader's header
   (`DriveFileUtility.CreateClientFileHeader`), upload, metadata update, and payload key headers
   (`DriveStorageServiceBase`) **(V)**.
4. Grants on a Private drive carry no key. `ExchangeGrantService.CreateDriveGrant` already takes
   a key source, and a keyless one exists (`NoStorageKeySource`) **(V)**. Every caller is still to
   be traced.
5. Consent carries the browser's locked drive keys: one more field, `ZkDriveKeys`, in `EncryptedTokenExchange`
   (see [Getting ZK drive keys to an app](#getting-zk-drive-keys-to-an-app)).
6. An owner-only conversion endpoint for steps 2 to 4 above.
7. An owner-only endpoint that adds a ZK copy of a drive's key without converting the drive (see
   [Every drive gets a ZK copy](#every-drive-gets-a-zk-copy)).

Owner console (browser): the second password key at login, the zero-knowledge master key,
sealing at consent, the conversion, and a ZK copy for every drive.

Apps: lock and open file headers with the drive key instead of the shared secret, and keep the
drive keys in the device key store.

### Not in this step

- **Deposits.** Chat, Mail and Feed receive files from peers, and today the receiving server
  re-locks them with the drive key (`PeerInboxProcessor`) **(V)**. They convert once deposits
  exist.
- Selective Share, and the recovery phrase redesign.
- Collaborative sharing, which doesn't change. Collaborative drives still get a ZK copy of their
  key (below).

### Every drive gets a ZK copy

Decided 2026-10-23: from step 4 on, every drive has a copy of its key locked by the
zero-knowledge master key, whatever its sharing model. Every app is handed the keys of the drives
it is granted, locked to the app at consent. Whether it uses them is up to the app.

- **A ZK copy is not conversion.** Collaborative and Selective Share drives keep their master-key
  copy, and the server keeps working with it as today. Only a Private drive loses its master-key
  copy, at conversion. Zero knowledge means "no master-key copy" (`StorageDrive.IsZeroKnowledge`),
  so a drive with both copies isn't mistaken for zero knowledge **(V)**.
- **New drives always get one.**
  - When the owner console creates a drive, the browser is there: it adds the ZK copy right away.
  - Some drives are created on the server with no browser present: built-in drives at first run
    and at version upgrades (`BuiltinProvisioner.EnsureDrivesAsync`, called from
    `TenantConfigService`) **(V)**. The login backfill below picks those up.
- **Existing drives, at the next login.** The browser lists the drives with no ZK copy. For each
  one, the server opens the key with today's master key and sends it locked to the owner's shared
  secret, as in step 2 of the conversion. The browser locks it with the ZK master key and sends
  the copy back. This is the same login step that converts the Shard Recovery drive today.
- **The server never had less.** For these drives it already holds the master-key copy, so
  sending the key to the owner's browser shows it nothing new.
- **When the copy no longer opens.** After a reset with the recovery key the ZK master key is new.
  The browser replaces the copy of every drive that still has a master-key copy. A Private drive
  has none, so only a recovery that gives back the second password key can reach it: Shamir does
  that in plan B, and the recovery phrase in step 3.

### First drive: Vault

- Owner only, and no server feature uses its content: the server code names it only in the
  built-in app and circle definitions (`BuiltinApps.cs`, `BuiltinCircles.cs`,
  `SystemAppConstants.cs`) **(V)**.
- **To check:** the built-in Vault circle ("Members can use your vault") grants ReadWrite on the
  Vault drive (`BuiltinCircles.cs`) **(V)**. A read grant carries the drive key, and a Private
  drive has no peer readers, so that circle loses read on conversion. Whether `OwnerOnly`
  already stops the grant being used is not checked.

### Catches

- **A forgotten password would lose the converted drives** without the new recovery. That's
  why recovery comes first (see [Order of work](#order-of-work)).
- **Apps lose read access to a converted drive until they sign in again.** Their grant copy is
  gone, and new keys only arrive through consent.

### Risk we accept: the old key may already have leaked

- Before conversion, the server opened the drive key in memory every time an app or the owner
  read the drive. Any of those moments could have leaked it.
- Conversion doesn't change the key. A key leaked earlier still opens old files and new ones,
  because new file headers are locked with the same key.
- So conversion gives "from now on, the server can't open it", unless the key had already leaked.
  That is the trade for re-encrypting nothing.
- **Fix, later and optional: key rotation.**
  - The browser makes a new random drive key, and new files use it. Old headers stay locked with
    the old key.
  - Each file header then needs a key version, so a device knows which drive key opens it.
    Devices hold both keys.
  - Re-encrypting old headers under the new key is a separate, optional bulk job. It is only
    worth doing if old files must also be safe from a key leaked in the past.

## Work for Chat and Vault

Chat and Vault are the only apps that change. Every other app keeps working as today, because
its drives stay Collaborative. Vault goes first, because it needs no deposits.

chat-kmp paths are relative to the chat-kmp repo (read 2026-10-22, at `13f06b94f`). `api/` means
`homebase-api/src/commonMain/kotlin/id/homebase/api/`.

### What chat-kmp does today **(V)**

- **One app, one sign-in.** Chat and Vault ship in one binary per platform and share one app
  registration (`APP_ID`, slug "chat"). Vault is a permission extension of that app
  (`homebase-common/.../config/AppConfig.kt`). One consent can carry both drive keys.
- **Sign-in** makes a P-384 key pair (`api/youauth/YouAuthFlowManager.kt`) and opens the token
  exchange with AES-CBC only (`api/youauth/YouAuthProvider.kt`).
- **Every file header is opened in two places:**
  - `ServerFile.asHomebaseFile` (`api/client/drives/ServerFile.kt`) opens the header with the
    shared secret. It serves file header, query batch, both websockets and temporal reads.
  - The payload and thumbnail response header (`sharedsecretencryptedheader64`) is opened in
    `DriveFileProvider.kt`.
- **Uploads** lock the header with the shared secret in `UploadDescriptorBuilder.kt` (create) and
  `DriveUploadProvider.kt` (update). The outbox re-keys to the header the server returns
  (`DriveOutboxUploader.kt`).
- **Chat sends** set the recipients and the server fans out
  (`homebase-chat/.../services/ChatMessageSenderService.kt`). No per-recipient key work, and no
  use of drive public keys.
- **Push text goes in the clear:** each chat send carries `unEncryptedMessage`, the notification
  text, to the server (`ChatMessageSenderService.kt`).
- **Vault** uploads and reads its own drive, shares with no one (`recipients = emptyList()`), and
  hands decrypted files to WebDrop. WebDrop uses its own drive, its own random link key, and
  stores the drop unencrypted on the server (`WebDropService.kt`). It isn't affected.
- **Where secrets live:** one `SecureStorage` abstraction (`api/storage/SecureStorage.kt`).
  - Android Keystore, iOS Keychain: good.
  - Desktop: a PKCS12 file with a hardcoded password (`SecureStorage.jvm.kt`).
  - Web: plain `localStorage` (`SharedPreferences.web.kt`).
  - The local database keeps each file's opened key header (`MainIndexMeta.kt`, `jsonHeader`),
    and the outbox keeps the plain key header (`OutboxSync.kt`). SQLCipher on Android, iOS and
    desktop. The web database is unencrypted, in memory.

### chat-kmp changes

1. **Sign-in:** open the new `ZkDriveKeys` field in the token response and keep the drive keys in
   `SecureStorage`. The sealed field should use AES-GCM, so the exchange code needs GCM.
2. **One switch for headers:** on a zero-knowledge drive, open and lock headers with the drive key
   instead of the shared secret. That's `ServerFile.asHomebaseFile`, the payload/thumbnail
   decrypt in `DriveFileProvider.kt`, `UploadDescriptorBuilder.kt` and the update descriptor.
   The outbox stops re-keying to a server-returned header.
3. **Vault:** nothing beyond 1 and 2.
4. **Chat sending (deposits):** fetch each recipient's chat drive public key (endpoint below,
   cached), lock the file key to it, and put one locked header per recipient in the upload.
5. **Chat receiving:** open a deposited header with the chat drive's private key. That private
   key is stored locked by the drive key (`WriteOnlyKeyPair`), so the app fetches it and opens it
   on the device.
6. **Push text:** a zero-knowledge chat can't send `unEncryptedMessage`. Either a generic
   notification, or the text as a deposit opened on the device (push decrypt is a TODO today,
   `homebase-common/.../notifications/NotificationService.kt`).
7. **Device storage becomes the security boundary.** The drive keys join the shared secret in
   `SecureStorage`. Web (`localStorage`) and desktop (hardcoded keystore password) are weaker
   than mobile. To decide: accept, harden, or keep zero-knowledge drives off web and desktop.

Not changing: peer reads, the peer websocket, temporal reads and sending to a peer's drive
(`PeerDriveUploadProvider.kt`). Those all reach other people's Collaborative drives.

### odin-core API changes

1. **Drive info carries `SharingModel`,** so a client knows which key opens a header.
2. **Uploads, updates and local metadata on a zero-knowledge drive:** the key header arrives
   locked with the drive key. The server stores it as is, and the upload response carries no
   re-locked header.
3. **Reads on a zero-knowledge drive return the stored header as is:** file header, query batch,
   websocket notifications (`ClientDriveNotification`) and the payload/thumbnail response header
   (`DriveStorageControllerBase`) **(V)**. The field keeps its name; its meaning follows
   `SharingModel`. To decide: keep the name or add a new field.
4. **Sign-in:** consent accepts the browser's sealed blob, and `YouAuthTokenResponse` gets one new
   field, `ZkDriveKeys` (`YouAuthTokenResponse.cs`) **(V)**. The YouAuth reference client too.
   Trusted apps that skip consent go through the consent page without a screen (see
   [Trusted apps](#getting-zk-drive-keys-to-an-app)).
5. **Grants carry no storage key on a zero-knowledge drive** (see
   [What changes](#what-changes)).
6. **Owner console:** password data, conversion, and creating a drive with its zero-knowledge
   fields.
7. **Chat deposits:**
   - An upload with recipients carries one locked header per recipient. The sending server
     forwards each as is, and the receiving inbox stores it as is instead of re-locking
     (`PeerInboxProcessor`) **(V)**.
   - The client fetches a recipient's drive public key with the existing endpoint
     `V2PeerAppDrivePublicKeyController.GetDriveWriteOnlyPublicKey` **(V)**.
   - The app fetches its own drive's `WriteOnlyKeyPair` private half, still locked, to open
     deposits.

## Decisions so far

- **V1: no key rotation when a member is removed.** Removing someone deletes their row, and the
  server stops serving them. Accepted risk: the removed person would also have to break into the
  server to read newer files.
- **No proxy re-encryption** (server-side "tickets", e.g. Umbral). It is novel crypto, and a
  colluding server plus member could recover the shared key.
- **The limit we accept:** you can't have exact per-file sharing, zero knowledge, and no per-file
  client work all at once. Selective Share gives up the last one for sharing existing files.
- **Ruled out:**
  - a drive-wide "share key": for reading content it's the same as handing out the drive key.
  - a header per member per file: the rows grow as files × members.
- **The password is asked for at login only.** The browser keeps the zero-knowledge master key
  for the owner session, locked by a session key the server holds. See decision 3 in
  [plan B](zero-knowledge-recovery-plan.md#decisions-first).
- **Trusted apps keep skipping the consent screen.** They pass through the consent page, which
  hands over the drive keys and continues on its own with no click.

## Open

- Key rotation after conversion: when, and the key version on file headers (see
  [Risk we accept](#risk-we-accept-the-old-key-may-already-have-leaked)).
- Splitting the app token into a login part and a key part. Today the token half is both **(V)**.
- Server features that need plaintext today, still to audit: feed distribution, email, profile
  publishing, guest browsing.
- How a sender finds the right receiving drive on the peer's side (via
  [drive-addressing.md](drive-addressing.md)).

## Recovery

- **Today:** recovery (phrase and Shamir) brings back only today's master key.

### Redesign recovery

- **Why:** the server can reach the phrase today **(V)**, so anything worked out from it could
  be worked out by the server too:
  - It is stored locked by today's master key (`MasterKeyEncryptedRecoverKey`,
    `PasswordKeyRecoveryService.cs`), which the server unlocks at every owner login.
  - Showing the phrase again unlocks it on the server.
  - Recovering sends it to the server (`VerifyRecoveryKeyRequest.EncryptedRecoveryKey`).
  - Shamir rebuilds it on the server, then emails a one-time link that unlocks it
    (`ShamirRecoveryService.cs`, `RecoveryNotifier.cs`).
- **The password itself is never on the server.** Login sends only a hash and today's password
  key (`PasswordReply`) **(V)**. So recovery can't just email the old password.

**Direction: recovery gives you your login back, instead of resetting it.**

- The browser stores your login keys locked, once per recovery route. The login keys are the
  password hash, today's password key (KEK) and the second password key. Not the password
  itself, which may be used elsewhere.
- Recovering opens them in the browser and logs in as normal, so both master keys unlock. Then
  it asks for a new password.
- Today's server-side reset goes away (`ResetPasswordUsingRecoveryKeyAsync`, the master key copy
  of the phrase).

**Recovery phrase:**
- A key worked out from the phrase in the browser locks one copy of the login keys. The server
  never sees the phrase.
- To show the phrase again, the browser keeps it locked by the zero-knowledge master key.
- Existing phrases are replaced once, because the server has already seen them.

**Shamir, dealt in the browser.** Dealing:
1. The browser makes a random Shamir secret and locks a copy of the login keys with it.
2. It splits the secret into shards.
3. It locks each shard to the holder's Shard Recovery drive public key: the same
   lock-to-public-key primitive. The holder's server stores it as received.

Recovering:
1. The recovering browser makes a throwaway key pair and starts recovery (email check, as today).
2. Each holder approves in their own owner console, as today (`ApproveShardRequest` is an owner
   API) **(V)**. The holder's browser opens the shard and seals it to the recovering browser's
   public key.
3. The recovering server only stores the sealed shards. The browser opens them, rebuilds the
   secret, opens the login keys and logs in.

Why each holder's public key, and not today's way: today the dealer's server keeps a key per shard
(`DealerShardEnvelope`) and opens the returned shards itself (`ShamirRecoveryService.PrepareFinalKeys`)
**(V)**. Many identities are on the same hosting, so the hosting could gather enough shards.

**Password change:** the browser locks the new login keys again for both routes. So it keeps the
phrase key and the Shamir secret, locked by the zero-knowledge master key.

**Catches:**
- The recovering browser must keep its throwaway private key from start to finish (in browser
  storage). Finishing on another device doesn't work unless the key travels too.
- A shard is safe from the holder's server only once the holder's Shard Recovery drive is zero
  knowledge. That drive converts in step 1, at the holder's first login, so a holder is ready
  only after that login.
- The dealer's server fetches each holder's public key, so a dishonest server could swap in its
  own. That is the same kind of limit as the server delivering the console code.

**Why first:** a forgotten password must never lose a zero-knowledge drive, so recovery is in
place before any drive converts (see [Order of work](#order-of-work)).

**Automated holders:** Shamir supports automated holder identities
(`AccountRecovery.AutomatedIdentityKey`, `ShamirConfigurationService.cs`) **(V)**. Their shard
opens on a server by design. To decide whether they stay.
