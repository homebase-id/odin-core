# Recovery in the browser: plan B

**Status:** the phrase pair is built on branch `zk-recovery-phrase` (odin-core and odin-js), and the
friends pair on `zk-recovery-friends`, which carries it ([As built](#as-built-friends-pair)).
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
    - **Decided for shards (F1, [friends pair](#building-the-friends-pair)):** a gated shard is a
      deposit, and zero-knowledge drives support deposits in the same PR.
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

## Building the friends pair

Planned 2026-10-24, built the same night ([As built](#as-built-friends-pair)). **Rule: follow today's C# code.** It has one gap, and it is the reason
for this pair: a gated friend's shard (a friend who must approve its release, `PlayerType.Delegate`)
isn't encrypted for that friend. Their server stores it and could release it without them. Every
other part of today's flow stays: the same files, peer calls, collectors and endpoints. It changes
only where zero knowledge forces it, and each change says why. Facts from reading the code are
marked **(V)**.

### Today's flow, which stays

- **Dealing** (`CreateShards` **(V)**): split the secret; encrypt each shard with its own random
  per-shard key (AES-CBC); send the holder a `PlayerEncryptedShard` (`Id`, `Index`,
  `RecoveryEmailHash`, `Player`, `Created`, `DealerEncryptedShard`) as an unencrypted file of type
  `74829` on its Shard Recovery drive. The per-shard keys stay with the dealer, in the dealer
  package (`DealerShardEnvelope`). So a holder never sees a raw shard, and nobody rebuilds anything
  without both the dealer package and the holders' files.
- **Recovery** (`ShamirRecoveryService` **(V)**): the email check, then `request-shard` to every
  player. An automatic player returns its shard at once. A gated player records a
  `ShardApprovalRequest`; the friend approves in the console, and their server sends the shard to
  the dealer (`accept-player-shard`). The dealer collects shards until it has enough.

### The changes

1. **A gated shard is sealed to the friend.** The dealer seals the `DealerEncryptedShard` bytes to
   the friend's Shard Recovery write-only public key. That key's private half is locked in the
   friend's browser (plan A: `ZkStorageKeyEncryptedWriteOnlyPrivateKey`, opened by the drive's
   storage key, which the ZK master key opens **(V)**). So only the friend's browser can open it,
   and what it opens is still locked by the dealer's per-shard key: the friend learns nothing, as
   today.
   - **As a deposit (F1, decision 10):** the shard file is `IsEncrypted = true`; its content is
     encrypted with its own file key, and that file key is sealed to the friend's public key.
   - The holder's checks read `Player`, `RecoveryEmailHash` and `Created` from the content today
     **(V)** (`GetShardStoredForDealer`, `HandleReleaseShardRequest`), and the server can't read
     encrypted content. So these move to the file's plain metadata.
   - Automatic shards aren't sealed: their server must release them with no human.
2. **The browser deals, not the server.** The Shamir secret now guards the ZK master key
   (decision 2), so the server must never see it. The browser does exactly what `CreateShards` does,
   plus the seal, and posts the result. The server runs the rest of `DistributeShards` as today:
   it saves the dealer package and sends the files.
3. **The friend's browser releases a gated shard.** On approve, the browser opens the ZK master
   key (session key, decision 8), then the drive key, then the write-only private key, then the seal.
   It seals the shard again to the recovering browser's throwaway public key and posts it. The
   holder's server sends it to the dealer with today's `SendPlayerShard` **(V)**.
4. **The recovering browser rebuilds the secret, not the server.** The dealer can't open a gated
   shard, so `PrepareFinalKeys` moves to the browser (decision 2).
   - At `initiate-recovery-mode` the browser makes the throwaway key pair and sends the public key.
     Making it there, not at the email link, matters: the link may open in another browser.
   - `request-shard` carries the public key, and the holder keeps it with the `ShardApprovalRequest`.
   - Once enough shards are in, the email says "your friends have approved; go back to the browser
     where you started". It carries no key (decision 11). Today's email carries the final key.
   - The browser fetches one bundle: the collected shards, the per-shard keys, and the Shamir copies.
     The server seals all of it to the throwaway public key, so only that browser can read it.
   - The browser opens everything, rebuilds the secret with the dealer keys as `PrepareFinalKeys`
     does, opens the password and shows it. If the password doesn't log in, it resets with the
     reset key, as phrase recovery does (F3).
5. **The automatic shards can never rebuild the secret on their own.** The dealer package and the
   automatic holders are all servers. If the automatic shards reached the threshold, servers alone
   could rebuild the secret, and so the ZK master key. The server refuses such a deal: at least one
   gated friend must always approve.
   - Today they can reach it **(V)**. The console's table (`ShamirDistributionDialog.tsx`,
     `validationMatrix`) gives 5 friends and a threshold of 3 as 2 gated and 3 automatic, and the
     3 automatic shards alone reach the threshold.
   - The table is fixed so that gated friends ≥ friends − threshold + 1: 3 of 5.
6. **The dealer seals only to a key that only the friend holds.** A friend whose Shard Recovery
   drive isn't zero knowledge yet (they haven't logged in since plan A) has a write-only private key
   the server holds.
   - The public-key response gains a flag saying the drive is zero knowledge.
   - Until it is, the console shows that friend as not ready for a gated shard, as readiness shows
     other problems today.
7. **A friend's own reset leaves their shard dead, and the dealer is told.** A reset with today's
   master key gives the friend's drive new keys (`ConvertShardRecoveryDriveAsync` takes a new
   write-only key pair **(V)**), so a shard sealed to the old key can't be opened.
   - The seal says which key it was sealed to (F5). The holder's verify call compares that with its
     current key, and the dealer's health check shows "deal again".

### Deposits: what "fully supported" means

A deposit is a file someone else puts on a zero-knowledge drive. The depositor encrypts the content
with a new file key and seals that file key to the drive's write-only public key. The server stores
the sealed file key as received, sends it on as is, and returns it as stored. It never opens or
re-locks it (decision 10).

Today every path that touches a file key assumes the server can open it with the drive's key. These
places change (from the code map, read 2026-10-24) **(V)**:

1. **Writing a file:** the server locks the key header with the drive's storage key
   (`DriveStorageServiceBase`, `EncryptKeyHeader`). On a zero-knowledge drive this throws
   `DriveIsZeroKnowledge` (plan A), so it must store a sealed key as given.
2. **The outbox opens the file key to send it:** it re-locks it with the connection's shared secret
   (`PeerOutgoingTransferService`, `CreateOutboxItems`). A deposit needs a slot that carries the
   sealed key untouched.
3. **Incoming transfers** need the drive's key for an encrypted file (`PeerDriveIncomingTransferService`,
   `hasStorageKey`); without it the file goes to the inbox, which hits 1.
4. **Reads re-lock the file key** for the reader (`DriveFileUtility.CreateClientFileHeader`). A deposit
   returns the sealed key as stored.
5. **Queries hide encrypted files** from a caller without the drive's key (`DriveQueryServiceBase`,
   `FilterVisibleAsync`). On a zero-knowledge drive the drive permission decides instead.
6. **Converting the Shard Recovery drive is refused** once any file on it is encrypted
   (`ConvertShardRecoveryDriveAsync`). A friend's reset converts it again, and the shards they hold
   die. That is expected (change 7): they are deleted, not left to block the conversion.
7. **The other callers of `EncryptKeyHeader`** (`FeedWriter`, `ContactService`) keep refusing
   zero-knowledge drives.

In the browser (js-lib): seal a file key to a public key and upload a deposit; open a deposit's file
key with the drive's write-only private key and read it.

So the order is: deposits on zero-knowledge drives, with tests of their own, then gated friend
shards on top. Both go in the friends pair, because neither is useful alone.

### Smaller fixes on the way

- **Only the shard's player can deliver it.** `HandleAcceptRecoveryShard` doesn't check that the
  caller is the shard's player **(V)**, so anyone connected could fill the collector with junk. It
  is touched anyway, so it gets the check.
- **No rotation for friend deals.** A password change doesn't change the secret (decision 2), so
  `RotateShardKeysIfNeeded` and the "rotation pending" flag apply to the automated deal only.
- **A password change seals the new password twice:** once for the phrase, once for the Shamir key
  pair (F3). `resetpasswd` carries both.
- **The automated deal is unchanged:** server dealing, `PrepareFinalKeys`, the finalize email and
  `FinalizeRecovery`, restoring today's master key (decision 6).

### Decisions to confirm

- **F1. A gated shard is a deposit** (decision 10). Decided 2026-10-24: "That's how we do it." Zero
  knowledge drives must fully support deposits first, and gated friend shards ship in the PR where
  they do. See [Deposits](#deposits-what-fully-supported-means).
- **F2. Automatic friends stay, below the threshold** (change 5). Decided 2026-10-24: automatic
  friends < the deal's minimum shards, so at least one gated approval is always needed.
- **F3. The Shamir secret takes the phrase's shape.** Decided 2026-10-24 (option C).
  - The shards rebuild a random 128-bit secret S.
  - S opens a private key, and that private key opens the password. The matching public key is
    stored, so a password change seals the new password to it without S, and the friends aren't
    touched.
  - The reset key is hkdf(S). With it, recovery repairs a junk copy, as phrase recovery does.
  - S also locks the ZK master key, so a reset keeps ZK data.
  - Rejected: keeping S readable by the ZK master key (anyone who once had the password reads
    every later one), and re-dealing at every password change (it fails while any friend can't be
    reached).
- **F4. Today's friend deals are removed at ship.** Decided 2026-10-24. Their gated shards aren't sealed,
  and production has only team and test identities on them (decision 5). The security page's red dot
  comes on and the owner deals again in the console. No login re-deal, so no waiting for holders to
  log in.
- **F5. The seal format gets a version byte and the key id.** Decided 2026-10-24 (not disruptive: the
  format exists only on this unmerged branch). The key id is the CRC32C
  of the public key, as `DrivePublicKeyResponse.PublicKeyCrc32` already offers it "so a sealer can
  label an envelope with the key it used" **(V)**. The phrase pair's seal changes with it; nothing is
  released yet. (Settles the open item above.)

### Work

odin-core:
1. Bring the branch up to date with main (the `OdinSlug` type, #2011).
2. **Seal format** v1: version, key id, then today's layout, in C# and JS, with one vector both check.
3. **Deposits on zero-knowledge drives** (the 7 places above), with tests of their own: write, send,
   receive, read and query a deposit; refuse a deposit to a drive that isn't zero knowledge.
4. **`configure-shards`** takes the dealt package from the browser: the player shards (gated ones
   sealed), the dealer envelopes, and the Shamir copies. It refuses a deal whose automatic shards
   reach the threshold, and a gated friend whose drive isn't zero knowledge. Server dealing
   (`CreateShards`) stays for the automated deal only.
5. **Public-key response:** the zero-knowledge flag. Readiness uses it.
6. **Recovery:** the throwaway public key from `initiate-recovery-mode` through `request-shard` to
   the `ShardApprovalRequest`.
   - `approve` takes the shard sealed to that key.
   - The accept call checks its caller.
   - At the threshold, friend deals get a ready state and the "go back" email.
   - A new recovery-mode read returns the bundle sealed to the throwaway key.
7. **Shamir copies** in the ZK record, beside the phrase copies: the key pair, the password copy,
   and the ZK master key locked by the secret. The reset key's master key copy sits beside the
   phrase's.
8. **Ship:** delete friend deals that aren't automated, with the red dot (F4). Exclude friend deals
   from rotation.
9. **Tests:** the Shamir suite rewritten for this flow with a C# "browser", not doubled. The cases:
   - a mixed deal recovered;
   - the password copy junk, then reset;
   - a refused automatic-only deal;
   - a friend not ready;
   - a friend's reset flagged;
   - a stranger's accept refused;
   - automated recovery unchanged.

odin-js:
1. **js-lib:** Shamir split and rebuild ported from `ShamirSecretSharing.cs`, with C# vectors; the
   seal v1; uploading and opening a deposit; dealing; releasing; rebuilding.
2. **Console:**
   - the deal dialog deals in the browser, with the fixed table;
   - approve opens and re-seals;
   - readiness shows friends that aren't ready.
3. **Recovery page:** the throwaway key at initiate, the "go back" wait, the rebuild, then the
   password or the reset, sharing the phrase screens.
4. **Password change:** the second sealed copy.

### Ship

Both pairs are released together (decided earlier). The friends pair goes on new branches,
`zk-recovery-friends`, made from `zk-recovery-phrase` in odin-core and odin-js. Their PRs target main
and carry the phrase commits until the phrase PRs merge (no stacked PRs). Nothing is released
between the two merges.

### Size

Inferred: odin-core ~1,800–2,800 lines with tests (deposits ~800–1,200 of it), odin-js
~1,000–1,400.

### As built (friends pair)

Built 2026-10-24/25 on `zk-recovery-friends` (odin-core and odin-js). Facts checked in the code or by a
test are marked **(V)**. What changed from the plan above, and the choices made while building:

**Seal v1** (`ZkSeal`, js-lib `zkSeal`): base64 of `0x01 ‖ key id (4 bytes, big-endian) ‖ throwaway
P-384 public key (97 bytes) ‖ 12-byte IV ‖ AES-GCM`. The phrase pair's seal moved to it.
- **The key id is not `PublicKeyCrc32`** (F5 said it would be). It is the CRC of the recipient's raw
  uncompressed point, which the browser has. `PublicKeyCrc32` hashes BouncyCastle's DER, and the same
  key has a different DER by origin: 441 bytes read from a JWK, 464 generated, 120 as standard SPKI
  **(V)** (a scratch program). So a browser can't reproduce it, and it isn't even stable per key.
- `CRC32C.CalculateCRC32C` is, despite its name, the IEEE CRC-32 that zlib computes: the check value
  of "123456789" is `cbf43926` **(V)** (a test pins it). The browser's CRC-32 matches.
- Cross-language vectors, both ways **(V)**: a WebCrypto seal C# opens, and the JS Shamir split C#
  rebuilds (`RecoveryPhraseTests`), and the C# split JS rebuilds (js-lib tests).

**Deposits on zero-knowledge drives.** All seven places, with one limit:
- The key header is an `EncryptedKeyHeader` of a new type, `Sealed` (12): `Iv` as is, `EncryptedAesKey`
  = seal(write-only key, IV ‖ AES key). Decrypting one gives a `SealedKeyHeader`, and re-locking that
  gives the sealed header back. So upload, peer receive, inbox, outbox and reads pass it through
  unchanged without new parameters.
- `Deposits` holds the rules. `StorageDrive.CanHoldDeposits` allows a zero-knowledge drive, or the
  transient drive, which only sends a deposit on. Any other drive refuses one
  (`DepositNeedsZeroKnowledgeDrive`, 9018), on upload, on receive, and at storage.
- A query returns a deposit without the drive key; the drive permission and the ACL decide.
- A deposit is written once (`DepositCannotBeUpdated`, 9019); it can be overwritten whole.
- Converting the Shard Recovery drive deletes the deposits sealed to the old write-only key. It
  still refuses files locked with the drive key.
- **Limit: a deposit has no payloads.** A payload's key header leaves in a fixed-size response
  header (`SharedSecretEncryptedHeader64`: IV 16, key 48, version 4), which a sealed one doesn't fit.
  Chat and Mail attachments will need that changed.

**`configure-shards`** takes `{ minMatchingShards, shards: [DealtShard], shamirCopies }`
(`ConfigureShardsRequest`). The server refuses:
- automatic shards that reach the threshold (`AutomaticShardsReachThreshold`, 9021);
- a gated friend whose Shard Recovery drive isn't zero knowledge (`ShardHolderNotZeroKnowledge`,
  9020), or a shard not sealed to their current key. It asks the friend's server for the key, with
  the new `IsZeroKnowledge` on `DrivePublicKeyResponse`.

A friend deal replaces the automated deal's master-key copy; an automated deal forgets the friend
copies. The server deals only the automated deal now.

**A gated shard on the holder** is a deposit. What the holder's checks read sits in plain metadata:
the shard id (`UniqueId`), the recovery email hash (`GroupId`), and when it arrived. Being a deposit
means `Delegate`. An unencrypted `Delegate` shard (an old server deal) is no longer released or
counted.

**Recovery:**
- The throwaway public key goes with `initiate-recovery-mode` and stays with the email's nonce. The
  email link (`GET verify-enter`) only opens the recovery page (`?enter=<id>`). The page posts the
  nonce with its key (`POST verify-enter`), and a different key is refused (`RecoveryNotReady`,
  9022).
  - Why (not in the plan): anyone can start recovery. Without this, someone could start it with a
    key of their own and wait for the owner to click that email; the friends would then seal their
    shards to the stranger's key.
- `request-shard` carries the key to the holder's `ShardApprovalRequest`. Approving posts
  `{ index, sealedShard }`.
- `accept-player-shard` takes a shard only from its own player, as the dealer package lists it.
- At the threshold a friend deal goes to `AwaitingRecoveringBrowser` and sends a link-free email.
  `GET recovery-bundle` returns the envelopes, the collected shards and the copies, sealed to the
  throwaway key (`ShamirRecoveryBundle`).
- Logging in ends recovery mode, as it always did.

**Review fixes (first round):**
- Entering checks the browser's key before it uses up the link (`TableNonce.PeekAsync`), so a wrong
  browser can't burn the email.
- Asking to exit keeps the throwaway key while collecting goes on.
- A gated shard handed in must be sealed to the throwaway key, so its holder's server can't count
  junk towards the threshold. The browser rebuilds with the dealer's own index (the envelope's), not
  the one a holder sends. It also tries subsets when one shard spoils the rebuild.
- The owner is told once, at the threshold; later approvals are still collected.
- A friend deal without its copies can't start a recovery (`PasswordRecoveryNotConfigured`): only an
  owner request removes it, and a locked-out owner makes none.
- Converting deletes the dead shards' release requests too.
- A password copy sealed to a key that isn't the current one is refused.
- The threshold is at least 2, and an index is 1 to n.
- A peer read of a deposit passes its sealed key header through (`ReEncrypt`).
- Sending still needs the drive key, except for a deposit.
- The page keeps the throwaway key until recovery ends, and makes a new one for a new recovery.

**Review fixes (second round):**
- An exit request once the friends approved leaves the state alone, so the bundle stays.
- An unused second link ("send it again") can't start a recovery under way over again.
- A link that carries no browser key (the exit email's, say) can't enter a friend deal.
- A reset with a reset key ends recovery mode, as a finalized recovery does.
- The page forgets the throwaway key as soon as the bundle is open.
- Deposits show as sealed in the drive explorer and are skipped by exports.

**Reset:** `POST authentication/zk-reset` (renamed from `zk-phrase-reset`; nothing was released) takes
either reset key. The friends' is hkdf-sha256(S, no salt, "homebase-shamir-reset-key"), 16 bytes. A
reset keeps the zero-knowledge master key.

**Copies** (`ZkKeyRecord`):
- `ShamirPublicKeyJwk`, `ShamirKeyEncryptedShamirPrivateKey`, `ShamirKeyEncryptedZkMasterKey`,
  `ShamirPublicKeyEncryptedPassword`, `ShamirResetKeyEncryptedMasterKey`.
- The password copy is missing after a deal until the next password login writes it (the console
  deals without the password). Status shows `ShamirPublicKeyJwk` / `HasShamirPasswordCopy`.
- A password change re-seals it.

**Ship (F4) and resets:** a friend deal is never rotated. On owner requests (where rotation runs),
a friend deal whose copies are missing is removed, and the security page's red dot comes on. That
covers old server-made friend deals at ship, and a reset with today's master key afterwards.

**Tests (V2):**
- `FriendRecoveryTests`:
  - a mixed deal gives the password back only in the recovering browser;
  - junk and missing copies are repaired with the friends' reset key;
  - entering from another browser is refused;
  - a friend's reset leaves their shard dead and the dealer is told;
  - only the shard's player can deliver it;
  - a password change re-seals and deals nothing;
  - a deal without copies is removed with the red dot.
- `DepositTests`: write, read, query, send and receive; refused on a drive that isn't zero
  knowledge; no payloads.
- The Shamir suite is rewritten for friend deals with a C# browser (`ZkFriends`). The automated
  rotation tests stay.
- `FailedShardRotationTests` went: friend deals don't rotate, and the gate keeps its unit test
  (`ShardRotationGateTest`).
- The takeover's delegate cases went: the server can't make an old friend deal any more.

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
