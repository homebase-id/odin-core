# Recovery in the browser: plan B

**Status:** the phrase pair is built on branch `zk-recovery-phrase` (odin-core and odin-js), in
review; the friends pair is next. Names and shorthand for every key: [owner-keys.md](owner-keys.md).
Steps 2 and 3 in the
[order of work](zero-knowledge-drives.md#order-of-work): friend Shamir and the recovery phrase,
together. Plan A, the keys and the Shard Recovery drive, is built and merged
([zero-knowledge-keys-plan.md](zero-knowledge-keys-plan.md)).

Markers: **(V)** verified by reading the code (2026-10-22 to 10-24). Line counts and estimates are
inferred.

## What we end up with

- **Recovery gives you back your password,** shown only in your own browser. You log in with it
  as usual, and may then change it. Nothing is reset on the server.
- **Two ways back, one design:** the recovery phrase and friend Shamir each open the zero-knowledge
  (ZK) master key, and the ZK master key opens the stored password. The server can open none of it.
- **Every owner is upgraded at a password login,** with no extra step: the recovery phrase they
  wrote down keeps working.
- **Automated recovery is unchanged:** it still restores today's master key, not ZK data.

## Decisions (2026-10-24)

1. **Recovery gives back the password text, not keys.** The user sees the password they forgot
   and can simply keep using it, and recovery reuses the normal login and password change.
   - The risk, exactly: whoever opens the recovery (enough friends working together, or someone
     who finds the phrase) learns the password itself, so accounts elsewhere that reuse it fall
     too. They already control the Homebase identity either way. Accepted.
   - The password is shown only in the browser that recovered it: never in an email, a URL or a
     server response the server can open.
2. **The phrase and the Shamir secret each lock the ZK master key; the ZK master key locks the
   password.** A password change re-locks the password copy only. The phrase and the shards don't
   change, because the ZK master key doesn't (plan A re-locks it under the new password). This
   drops today's re-dealing after every password change for friend setups.
   - **Changed for the phrase** ([below](#changed-the-phrase-not-the-zk-master-key-guards-the-password)):
     the password is sealed to a key pair the phrase locks, not locked by the ZK master key, which
     anyone who once had the password holds. The friends pair must give the Shamir secret the same
     shape: its own key pair and its own sealed password copy.
3. **The recovery phrase the owner wrote down is kept** (decided 2026-10-24), so nobody writes
   down a new one. At a password login the browser takes it over, and from then on the server
   can't reach it.
   - Today the server knows the phrase **(V)** (`PasswordKeyRecoveryService.cs`): it makes it
     (`CreateInitialKeyAsync`, 16 random bytes), keeps it locked by the master key
     (`MasterKeyEncryptedRecoverKey`) and opens it when the owner views it (`GetRecoveryKeyAsync`),
     and recovery sends the phrase text to it (`AssertValidKeyAsync(text)`).
   - **The takeover:** the server opens its copy one last time for the browser. The browser locks
     the ZK master key with the phrase, and locks the phrase with the ZK master key so it can be
     shown again; the server stores both and can open neither. The server then deletes both of its
     own copies (`MasterKeyEncryptedRecoverKey` and `RecoveryKeyEncryptedMasterKey`). From then on
     the phrase never reaches the server: recovering, checking and showing it happen in the
     browser.
   - **The risk we accept:** the server saw every existing phrase before the takeover (made,
     viewed, checked, used). A server that kept a copy then can open ZK data later. It's the same
     risk as converting a drive with its existing key ([Risk we accept](zero-knowledge-drives.md#risk-we-accept-the-old-key-may-already-have-leaked)):
     from the takeover on the server can't reach it, unless it was copied before.
     `PasswordKeyRecoveryService` stores only locked copies **(V)**; logs and backups are not
     audited.
   - **New identities** get a phrase made in the browser at the first login, so the server never
     sees theirs.
   - **Changing the phrase isn't possible today** **(V)**: the server makes one only when none
     exists (`CreateInitialKeyAsync`, called only at setup from `TenantConfigService.cs`), and the
     console's "Generate New Recovery Phrase" button only reveals the existing one
     (`request-recovery-key`). A "make a new phrase" action, in the browser, is a roadmap item, not
     part of B.
4. **The upgrade happens at a password login,** the one moment the browser has the password: the
   password copy, the phrase takeover and a friend re-deal (see [At every password login](#at-every-password-login)).
5. **Friend Shamir is replaced, not run beside the old path.** Production counts (2026-10-24,
   EU1, NA1, Hetzner): every dealer outside the automated pattern is a team identity or a test,
   plus at most one other person, whom we contact. The old friend path goes in the same change:
   no flag, no two test suites.
6. **Automated recovery stays: it restores today's master key, not ZK data** (Homebase-run
   holders, no approval; `ConfigureAutomatedRecovery` **(V)**). One change: today it rebuilds the
   recovery phrase (`ShamirDistributionKeyEncryptedRecoveryKey`, `PrepareFinalKeys` →
   `DecryptRecoveryKey` **(V)**), and the phrase now opens the ZK master key, so Homebase's holders
   could open ZK data. So automated shards lock the master key with a key of their own instead
   of the phrase. Existing setups switch at their next re-deal, which already runs after a password
   change (`RotateShardKeysIfNeeded` **(V)**), or at the takeover. Moving automated recovery to the
   password would let Homebase recover everything by design; that's a separate decision.
7. **The old server-side reset with the phrase ends at the takeover** (settled with decision 3).
   Before an owner's takeover it works as today. After it, the phrase recovers the password
   instead, and the server holds no way in through the phrase.
8. **The password is asked for at login and password change only, as today.** The browser keeps
   the ZK master key for the owner session (the ZK session key), so Shamir setup, approving a
   shard and showing the phrase again never ask.
   - How it's kept: `localStorage` holds the ZK master key locked by a random session key. The
     session key lives in the owner's token record on the server (`OwnerConsoleClientRegistration`
     **(V)**), is returned only on a request carrying the HttpOnly cookie, and the page keeps it in
     memory, never in storage. Each half alone is useless, and logout or expiry deletes the token
     record and so the session key.
   - The limit, compared with today's split: today the server combines the cookie's half key with
     its token record (`AccessTokenHalfKey` **(V)**), so a script in the page never sees the master
     key. With zero knowledge the browser must open the key, so a script running in the logged-in
     page can use it while it runs. Later hardening: keep the opened key as a non-extractable
     WebCrypto key, so a script can use it but not copy it out (roadmap; js-lib passes raw bytes
     today).
   - Why not ask each time: browsers prefill the password, so a prompt protects almost nothing.
   - The limit: malware on the machine, or JS served by a bad server, can read it during the
     session. Both can read a typed password too.
9. **Friend shards are sealed to the holder's Shard Recovery write-only key,** which plan A made
   zero knowledge. No extra key pair.
10. **An encrypted file on a ZK drive is always `IsEncrypted = true`** (decided 2026-10-23). A
    sealed shard is the first **deposit**: its content is encrypted with the file's own key (the
    KeyHeader), and that file key is sealed to a public key: the holder's write-only key when
    dealing, the recovering browser's throwaway key when released.
    - Chat, Mail and Feed deposits reuse the format later, so the sealed file key says which
      public key opens it.
    - The server stores the sealed file key as received and returns it as stored; it never
      re-locks it.
11. **Emails carry nothing secret.** They say what happened and where to go next.

## The keys

What the server stores. It can open none of the new rows.

| Stored | Locked by | Opened when |
|---|---|---|
| ZK master key (plan A) | second password key | every password login |
| **Password** (new) | sealed to the phrase's public key ([changed](#changed-the-phrase-not-the-zk-master-key-guards-the-password)) | recovery; written by the takeover, a password change, or a login that finds it missing |
| **ZK master key, phrase copy** (new) | phrase key | recovery with the phrase |
| **Phrase key** (the existing phrase, new copy) | ZK master key | showing the phrase again |
| **Phrase key pair** (new): public half as is, private half locked | phrase key | recovery with the phrase opens the password |
| **Master key, reset copy** (new) | reset key = hkdf(phrase key) | recovery with the phrase, when the password copy doesn't log in |
| Phrase, server copies (today) | master key; the phrase locks the master key | deleted at the takeover |
| Automated recovery's key (new) | master key; locked by the automated Shamir secret | automated recovery |
| **ZK master key, Shamir copy** (new) | Shamir secret | recovery with friends |
| **Friend shard** (new, on each holder) | file key sealed to the holder's write-only key | the holder approves |
| Login keys (plan A) | ZK master key | removed by B: the password replaces them |

- The phrase key is today's 16 random bytes, shown as 12 BIP39 words. It's random, so it needs
  no stretching.
- The Shamir secret is not stored. Each deal makes a new one, and a password change doesn't need
  it (decision 2).

## Flows

### At every password login

After plan A's step (the ZK master key is open), inside the same 10 s cap. A failure never fails
the login; the next login retries.

1. **Password copy.** Sealed to the phrase's public key, so the browser can't open it to compare.
   It writes one only when the status says it's missing (after the phrase was typed in again, or a
   password change that sent none).
2. **Phrase takeover,** if not done yet (decision 3): the server hands over its copy of the
   phrase once, the browser locks the ZK master key with it and locks the phrase with the ZK master
   key, and the server deletes its own copies. An identity with no phrase yet gets one made by the
   browser, and the console shows it to write down (today's first-view flow, `RequestRecoveryKey`
   **(V)**).
3. **Friend re-deal,** if the owner has an old server-dealt friend setup: the browser deals again
   to the same friends with the same threshold (see [Dealing](#dealing-to-friends)), once every
   holder's Shard Recovery drive is converted. Until then the console shows who hasn't logged in
   since plan A, and the next login tries again. Automated setups are left alone.

### Password change

Plan A re-locks the ZK master key under the new password. B adds the new password, sealed to the
phrase's public key, to the same `resetpasswd` request; the server saves both in one transaction.
Nothing else changes.

### Showing the phrase again

The browser opens the phrase key with the ZK master key and shows the words. The server keeps
today's waiting period by releasing the locked phrase key only once it's over (today:
`NextViewableDate` **(V)**). Checking the phrase (`VerifyRecoveryKeyAsync`, a server-side check
today **(V)**) moves to the browser: open the phrase copy.

### Recovery with the phrase

1. On the login page, "Forgot password", then the 12 words.
2. The browser fetches the phrase copy of the ZK master key, the phrase's key pair and the
   password copy. No login is needed: all of it is useless without the phrase, and a 128-bit key
   can't be guessed.
3. The browser opens the phrase's private key, then the password, and shows it.
4. The owner logs in as usual, and may change it.
5. If there is no password copy, or it doesn't open or doesn't log in (someone with the password
   sealed junk), the browser derives the reset key from the phrase and sets a new password with it,
   sent encrypted to the host's key; the server resets the password with today's master key, the
   ZK master key (from its phrase copy) is locked by the new password, and every owner session ends.

Until step 5 the server changes nothing.

### Dealing to friends

At setup in the console (session key, decision 8), or at a login re-deal:

1. The browser makes a random Shamir secret and locks the ZK master key with it.
2. It splits the secret, fetches each holder's Shard Recovery write-only public key, and seals one
   shard to each as a deposit (decision 10).
3. The server stores the Shamir copy and sends each shard to its holder on the existing path: a
   file on the holder's Shard Recovery drive (`transferService.SendFile`) **(V)**.

### Recovery with friends

1. The recovering browser makes a throwaway key pair, keeps the private key in browser storage,
   and starts recovery with the public key. The email check stays as today.
2. Each holder's server records the request with that public key. The holder approves in their
   console (no prompt, decision 8). Their browser opens the drive key with the ZK master key, then
   the write-only private key, then the shard, and seals it to the throwaway key.
3. The recovering server collects the sealed shards, which it can't open, and emails "your friends
   have approved; go back to the browser where you started".
4. That browser fetches the sealed shards, the Shamir copy and the password copy, rebuilds the
   secret, opens the ZK master key and then the password, and shows it.
5. Recovery mode ends, and the owner logs in as usual.

No `FinalizeRecovery` and no reset for friend setups.

### After an old-path reset

Automated recovery, and the old phrase before its takeover, reset the password with today's
master key (plan A then deletes the ZK keys **(V)**). B also deletes every copy tied to the lost ZK
master key: the password, the phrase copy, the browser's phrase copy and the Shamir copy.

- The next login makes a new ZK master key and re-deals friend shards.
- The phrase is still valid, but now nobody holds a copy of it but the owner. The console asks the
  owner to type it once to keep it (or, once the roadmap action exists, make a new one).
- ZK data is lost. Today that's only Shard Recovery, which holds nothing encrypted.

## Today, for reference

All verified by reading the code (`ShamirConfigurationService`, `ShamirRecoveryService`,
`RecoveryNotifier`).

- **The dealer's server deals** both kinds of shard: it splits the secret, locks each shard with a
  random per-shard key, sends the locked shard to the holder, and keeps the per-shard keys in its
  dealer package, an unencrypted file on its own Shard Recovery drive.
- **Automated holders** return the shard at once on `request-shard`. **Friend holders** record an
  approval; the friend approves in their console, which checks only that they are the owner and
  opens nothing.
- **The dealer's server rebuilds the secret** from the returned shards (`PrepareFinalKeys`), opens
  the recovery phrase, locks it with a one-time key, and emails a link with that key ("We have
  assembled your recovery key!"). The link plus the server's copy is the phrase.
- **So today's shards can be harvested from storage:** the per-shard keys and the shards are both
  unencrypted files. With a database or backup holding the dealer and enough holders, anyone can
  rebuild the phrase and so the master key.
- **Every shard has one file type** (`74829`), whatever the player type; only the dealer package
  records which players are automated.

## odin-core

- **Recovery record** (KeyValue, beside plan A's ZK keys): the password copy, the phrase copy and
  the browser's phrase copy, the next-viewable date, and the Shamir copy.
  Plan A's login-keys copy (`ZkMasterKeyEncryptedLoginKeys`) is removed.
- **Endpoints:**
  - owner: the phrase takeover (hand over once, save the browser's copies, delete the server's);
    save the password copy at login; read the browser's phrase copy after the waiting period;
  - `resetpasswd` carries the new password copy;
  - no login: the phrase copy and the password copy, for phrase recovery;
  - recovery mode: the sealed shards, the Shamir copy and the password copy.
- **The phrase's server copies** are deleted at the takeover, which ends the server-side reset with
  the phrase for that owner (decision 7). An old-path reset deletes B's copies.
- **Setup stops making the phrase** (`TenantConfigService.cs` → `CreateInitialKeyAsync` **(V)**);
  the browser makes it at the first login.
- **Automated recovery** locks the master key with its own key instead of the phrase
  (`ShamirKeyRecord`, `ShamirConfigurationService.cs` **(V)**), at its next re-deal.
- **Shamir** (`ShamirConfigurationService.cs`, 654 lines; `ShamirRecoveryService.cs`, 516
  lines **(V)**):
  - `configure-shards` takes the browser's sealed shards and the Shamir copy;
  - server dealing, `DealerShardEnvelope` and rotation (`RotateShardKeysIfNeeded`, run on every
    owner request when the password is newer than the shards **(V)**) stay for automated setups
    only;
  - `EnterRecoveryMode` carries the throwaway public key; the holder's release takes the sealed
    shard; `PrepareFinalKeys`, the "assembled" email and `FinalizeRecovery` stay for automated
    only;
  - peer calls (`ShamirPasswordRecoveryPeerController.cs`: `request-shard`, `accept-player-shard`)
    carry the public key and sealed shards.
- **Deposits on ZK drives** (decision 10). The server stores and returns the sealed file key as
  is wherever a shard passes:
  - incoming peer files: the `IsEncrypted` branch needs the drive key today
    (`PeerDriveIncomingTransferService.cs`, `hasStorageKey`) **(V)**;
  - reads: building the reader's header re-locks the file key
    (`DriveFileUtility.CreateClientFileHeader`) **(V)**;
  - queries hide encrypted files from a caller without the drive key
    (`DriveQueryServiceBase.cs`, `isEncrypted && hasStorageKey`) **(V)**; on a ZK drive the drive
    permission decides instead;
  - plan A's refusal of encrypted uploads (`DriveIsZeroKnowledge`) becomes "refused unless the file
    key is sealed".
- **ZK session key:** made at login, kept with the owner token, returned to the logged-in owner,
  dropped at logout and expiry.
- **Readiness and health** (`ShamirReadinessCheckerService.cs`, `OwnerSecurityHealthService.cs`,
  `SecurityHealthCheckJob.cs`) **(V)**: a holder is ready when its Shard Recovery drive is ZK;
  shard checks become "is the sealed file there"; the phrase check moves to the browser.
- **Tests:** a C# "browser" that takes over phrases, deals, seals, opens and rebuilds, like
  `OwnerPasswordFlow.cs` does for login. The Shamir suite (`tests/apps/Odin.Hosting.Tests.V2/Ported/Shamir/`,
  fixture 453 lines, about 18 tests) **(V)** is rewritten for friends, not doubled.

## odin-js

- **js-lib:**
  - BIP39 encode and decode, with the 2048-word list vendored (no new npm dependency); none exists
    in odin-js today **(V)**. Vectors from `BIP39Util` (`BIP39.cs`) in C#.
  - Shamir split and rebuild, ported from `ShamirSecretSharing.cs` (140 lines: a 257-bit prime)
    **(V)** to `BigInt`, with vectors from C#.
  - Seal and open to a public key: a throwaway P-384 pair, ECDH plus salt, AES-GCM. The building
    blocks exist (`createEccPair`, `getEccSharedSecret`) **(V)**; an "open" helper doesn't.
- **Login** (`ZeroKnowledgeProvider.ts`): the three steps above, after plan A's.
- **Session key:** keep the ZK master key locked by it; clear it at logout.
- **Phrase screens** (`ShowRecoveryKey.tsx`, `RevealRecoveryKeySection.tsx`,
  `VerifyRecoveryKeyDialog.tsx`, `AccountRecovery.tsx` **(V)**): show the phrase from the browser,
  check it, recover with it, and type it once after an old-path reset.
- **Shamir screens** (`ShamirDistributionDialog.tsx`, `ApproveAndReleaseShardsTabs.tsx`,
  `ShamirAccountRecovery.tsx` **(V)**): deal, approve, and recover with a throwaway key.
- **Password change:** add the new password copy.

## One PR or several

The parts and what each needs:

| Part | Touches | Needs |
|---|---|---|
| Session key | every owner session | nothing |
| Password copy, phrase takeover, phrase recovery | every owner | session key |
| Deposits on ZK drives | Shard Recovery drives | nothing |
| BIP39, Shamir, seal/open in js-lib | nothing until used | nothing |
| Friend dealing, approval, recovery, re-deal; old friend path removed | the team | all of the above |

**Recommended: two pairs of PRs (core + js each), released together or apart.**

1. **Phrase:** session key, password copy, phrase takeover, phrase recovery, automated recovery
   off the phrase, login-keys copy removed. Every owner gets it, so it needs the careful UX and the hardest look
   in review. Single-identity tests.
2. **Friends:** deposits, Shamir and seal/open in js-lib, dealing, approval, recovery, the login
   re-deal, old friend path removed. Only the team uses it. Multi-identity peer tests.

Why not one: about 4,000–6,000 lines in one review (inferred), two very different blast radii
(every owner vs. the team), and two kinds of test. Why not more: the parts inside each pair are
useless alone (a deposit nobody makes, a phrase nobody can recover with). The upgrade still happens
"at a password login" either way; released together, it's the same login. One PR remains possible
if you'd rather review once; it costs nothing in design, since the record is designed here for both.

## Building the phrase pair

Settled 2026-10-24. Verified facts from reading the code are marked **(V)**.

**Choices:** (A) setup keeps making the phrase and the first login takes it over, seconds later,
instead of the browser making it at setup; (B) no "recovery data was fetched" email; (C) the
locked ZK master key in `localStorage`, the session key in memory only.

odin-core:
1. **ZK record** (plan A's KeyValue record): the login-keys copy becomes the **password copy**,
   carried by create-keys and by the password change. It gains the two **phrase copies**. A
   password change merges into the record instead of rebuilding it. An old-path reset already
   deletes the record **(V)**, so B's copies go with it.
2. **Takeover** (owner): the server hands over the phrase once; the browser posts both phrase
   copies; in one transaction the server stores them, deletes its two phrase copies, switches
   server-dealt Shamir to the master key (3), and stores a **check value** (a hash of the phrase
   key) so a phrase typed in later can be checked without the server seeing it.
3. **Server-dealt Shamir locks the master key, not the phrase.** Today `SaveDistributableKey`
   reads the phrase on every deal and rotation, and `FinalizeRecovery` resets the password with
   the phrase text **(V)**; both break once the server has no phrase. The distribution key locks
   the master key instead, and finalize opens it. Existing setups are dealt again at the takeover,
   to the same players with the same threshold (a deal that can't be repeated is removed; see
   "As built"). (`MasterKeyEncryptedShamirDistributionKey`
   actually holds the phrase **(V)**; it goes.)
4. **Recovery read** (no login): the phrase copy of the ZK master key and the password copy, or
   "not taken over", and the page falls back to today's reset. The global per-IP rate limiter
   covers it (`AddIpRateLimiter` **(V)**).
5. **Showing and checking:** after the takeover the reveal returns the locked copy, with today's
   waiting period; the health check compares the check value.
6. **After an automated reset:** the console asks for the phrase once; the server compares the
   check value and stores fresh copies.
7. **Session key:** made at login in the token record, returned to the logged-in owner, gone with
   the record.
8. **Tests (V2):** takeover; recovery read before and after; old reset refused after; automated
   recovery and rotation after; password change keeps the phrase copies; reset deletes them;
   re-entry; session key lifetime.

odin-js:
1. **js-lib:** BIP39 with the vendored word list and C# vectors; the check value; the password
   copy with plan A's lock and open.
2. **Login:** create keys with the password copy, refresh it, take over, ask for the phrase after
   a reset.
3. **Session key:** the locked ZK master key in `localStorage`, cleared at logout.
4. **Password change:** carries the new password copy.
5. **Screens:** reveal from the locked copy, check in the browser, recover on the forgot-password
   page (falling back to the old reset), re-enter after a reset.

**As built (2026-10-24, phrase pair):**
- Endpoints: `GET security/zk` (status, with the password copy and `RecoveryPhrase`: none, onServer,
  takenOver, needsReentry), `POST security/zk/password`, `POST security/zk/phrase-handover`,
  `POST security/zk/phrase-takeover`, `POST security/zk/phrase-reentry`, `GET security/zk/session-key`,
  and, with no login, `GET authentication/zk-phrase-recovery`.
- The handover and the password copy need proof of the password itself, a `PasswordReply` against
  a fresh nonce, as a password change takes it (`OwnerSecretService.AssertValidPasswordAsync`); the
  login has the password, so the owner is never asked. The takeover and the re-entry need the
  phrase's check value, so copies made with any other phrase are refused. A stolen session can do
  none of them. (A first build used a 5-minute window after login instead; replaced in review.)
- The phrase copies and the password copy sit in plan A's ZK record, so a reset with today's master
  key forgets them with the ZK keys. The check value and `TakenOver` sit in the phrase's own record
  and survive it.
- `ZeroKnowledgeRecoveryService` holds the password copy and the takeover, because they span the
  password, the phrase, the ZK keys and server-dealt Shamir.
- The health check sends only the check value, in every state; before the takeover the server
  works out the expected one from its own copy.
- Server-dealt Shamir: `ShamirKeyRecord.ShamirDistributionKeyEncryptedMasterKey`; the finalize nonce
  carries the distribution key. A recovery started before the deploy and finalized after it fails
  and must be started again (no compatibility for the old nonce).
- odin-js: BIP39 in js-lib with the word list copied from odin-core; the forgot-password page tries
  the phrase first and falls back to `resetpasswdrk`; the reveal opens the locked copy; the health
  check sends the check value; the security page asks for the phrase after a reset. The login's
  three reads (status, drive, session key) run together.

**Review fixes (2026-10-24):**
- **A reset key, so recovery doesn't rely on copies a hijacker can rewrite.** Anyone with the
  password can rewrite the password copy (a password change writes it), and the server can't check
  a copy it can't open. So the browser also sends a reset key, one way, and the server locks today's
  master key with it itself (`ZkKeyRecord.ResetKeyEncryptedMasterKey`), correct by construction.
  Recovery: if the password copy logs in, done; otherwise the browser sends the reset key with a new
  password and the ZK master key locked by it (`POST authentication/zk-phrase-reset`, no login) and
  the server resets the password as today's reset does, keeping the zero-knowledge keys. The first
  version derived the reset key from the ZK master key; the second review changed that, see below.
- **The takeover also needs the password itself**, and the browser opens its copies again before
  they go.
- **An old deal that can't be dealt again is removed**, not left blocking the takeover at every
  login: without the phrase it couldn't recover anything. The security page's red dot comes on
  (`needs-attention`: not configured). The re-deal runs in its own transaction, at the handover
  since the second review, and shares the rotation gate.
- **Finalizing a server-dealt recovery is one transaction**, and recovery mode ends last, so a
  failure keeps the link and the collected shards.
- **The handover skips the 14-day reveal wait once per owner** (accepted): the one exposure is an
  attacker who logs in with a phished password before the owner's next login.
- `Development:DealsLockThePhrase` makes deals lock the phrase until the takeover, as every deal did
  before, so tests start where existing owners are.

### Changed: the phrase, not the ZK master key, guards the password

> **Second review (2026-10-24). Read this before anything above about the password copy or the reset
> key.** The terminology document will explain it again, step by step.

**The hole.** The first version locked the password copy with the ZK master key and derived the
reset key from it. A password change keeps the ZK master key, and anyone with the password (and a
session) opens it. So someone who learned the password *once* kept, forever and with no login:
- every later password, by reading the no-login recovery copy;
- a password reset, with the reset key;

and needed no phrase for either, which skipped the 14-day wait before the phrase is shown.

**The fix.** Both now hang off the phrase:
- **Reset key** = hkdf-sha256(phrase key, "homebase-recovery-reset-key"), 16 bytes. Only the phrase
  gives it.
- **Phrase key pair** (P-384), made by the browser whenever it has the phrase (takeover, re-entry).
  The public half is stored as is; the private half is locked by the phrase key.
- **Password copy** = the password sealed to the phrase's public key. Sealing needs only the public
  key, so a password login or change writes it without the phrase; only the phrase opens it.
- The reset key travels encrypted to the host's offline key, as `resetpasswdrk` sends the phrase.

**What it buys.** Someone who once had the password can still seal junk as the copy (the reset key
repairs that), but can neither read the next password nor reset it without the phrase, which takes
the 14-day wait, as today. The ZK master key itself still never changes, like today's master key;
using it needs a login with the current password.

**Consequences in the code:**
- The browser can't open the copy to compare, so a login writes it only when it's missing.
- Re-entering the phrase makes a new key pair but has no password to seal; the next login writes it.
- The no-login recovery read returns the phrase copy of the ZK master key, the key pair and the
  copy; with no copy, or one that doesn't open, the page goes straight to "set a new password".
- Any password reset ends every owner-console session (`OwnerSecretService.ResetPasswordUsingMasterKeyAsync`),
  so an intruder's console session goes with it (other tokens: #2005).
- A login can't tell a good copy from junk (only the phrase opens it), and no check value can:
  whoever writes junk knows the password and could make the matching check. Copies don't go stale
  on their own (a password change writes the new one, a reset with today's master key deletes the
  record, a reset with the phrase writes it, a login fills a missing one). Junk is repaired by the
  reset key at recovery. Decided 2026-10-24: no check value, no rewrite at every login.

**Other second-review fixes:**
- The old deal moves off the phrase **at the handover**, before the phrase is sent: if moving fails,
  the phrase isn't handed over, so a failure that repeats doesn't hand it over (skipping the
  14-day wait) at every login. Moving early is harmless: a deal locking the master key is how
  every deal works now. The takeover checks again.
- The re-deal waits for a running rotation instead of refusing, ignores the rotation's one-hour
  failure cooldown, checks the deal again inside the gate, and deletes inside it
  (`ShardRotationGate`, a semaphore).
- An old deal is removed only for a permanent cause, checked before dealing: a friend no longer
  connected, automated recovery off on this host, or no dealer package. Any failure of the deal
  itself throws, and the next login retries.
- The takeover checks its own preconditions before touching the deal, which can't be undone.
- The no-login recovery read answers 409 when a reset with today's master key forgot the phrase
  copies, so the page says the phrase can't recover until it's typed in again, instead of sending
  the owner into the old reset, which refuses a taken-over phrase.
- A reset ends owner-console sessions in its own transaction. App and YouAuth tokens, and an
  intruder's Shamir deal or recovery email, are #2005.
- The phrase key must be P-384; a malformed reset request is a 400, not a 500.
- A reveal that fails (the phrase waits to be typed in again) no longer spends the one free view.

**Open for the friends pair** (nothing is released, so it costs nothing to change before then):
- The seal format (throwaway P-384 public key ‖ AES-GCM) has no version byte and doesn't say which
  public key opens it. Decision 10 wants a deposit to say so, and the friends pair makes the
  deposit format, so it settles both there: add them to this format, or seal deposits another way.
- The friends pair removes the old friend path; the takeover's re-deal then handles automated
  deals only.

## Size estimate

Inferred. Lines include tests.

| Part | odin-core | odin-js |
|---|---|---|
| Phrase pair | ~800–1,300 | ~700–1,000 (incl. the 2048-word list) |
| Friends pair | ~1,500–2,500 | ~800–1,200 |
| SQL generator | none (KeyValue) | |

## Risks

- **The server saw every existing phrase before the takeover** (decision 3, accepted).
- **Owners who never log in with a password after B** keep the old phrase reset, which doesn't
  restore ZK data. The takeover must be done before any drive with encrypted files converts
  (step 4) for that owner.
- **The Shamir test rewrite** is the biggest odin-core item.
- **Friend recovery must finish in the browser that started it,** which holds the throwaway key.
  A lost phone means starting again, and the friends approve again.
- **Friend re-deal waits for holders** to log in once since plan A shipped.
- **Unauthenticated phrase-recovery read:** safe against guessing (128-bit key), but it tells
  anyone that an identity's phrase has been taken over. Rate-limit it.
- **odin-js installs:** `npm install` needs GitHub Packages auth for `@homebase-id/ffmpeg`
  (README) **(V)**. The plan adds no npm dependency.

## Out of scope here

Everything in plan A, converting any drive besides Shard Recovery, deposits other than shards
(the format is set here, decision 10), Chat, Selective Share, moving automated recovery to the
password (decision 6), and changing the phrase (roadmap, decision 3).
