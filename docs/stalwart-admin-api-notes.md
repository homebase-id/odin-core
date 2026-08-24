# Stalwart management API — live-verified wire contract

Probed live against **Stalwart 0.16.18** (docker `stalwartlabs/stalwart:v0.16`,
local dev instance per `docker/stalwart-dev/compose.yml`) on 2026-08-21.
This is the CreateZone-style validation demanded by `docs/email-keys-plan.md`:
every `IMailboxProvider` operation exercised by hand before code hardened
around assumptions; `StalwartMailboxProvider` encodes this contract and the
docker-gated `StalwartMailboxProviderTests` re-verify it against a real
container.

## Big picture (differs from what doc-2 guessed)

- v0.16 has **no REST admin API for provisioning**. Everything is a
  **JMAP-based registry**: object types are managed with per-type JMAP methods
  (`x:Domain/set`, `x:Account/get`, ...) under capability `urn:stalwart:jmap`,
  POSTed to `/jmap` (basic auth works). NOT `Registry/set` (DeepWiki is stale).
- The registry is **self-describing**: `GET /api/schema` (gzipped JSON, ~940KB)
  lists all 150 object types, their fields, types, and serverSet/mutable flags.
  When something rejects a payload, the schema has the answer.
- **Headless install**: write `config.json` = `{"@type":"RocksDb","path":"/var/lib/stalwart/"}`
  to `/etc/stalwart/`, boot with `STALWART_RECOVERY_MODE=1` +
  `STALWART_RECOVERY_ADMIN=admin:devadminpass`, provision via the API, then
  drop the recovery env for normal operation. First boot without config.json
  is "bootstrap mode" (wizard only, API 404s).

## The six provider operations, verified

All calls: `POST /jmap`, body `{"using":["urn:ietf:params:jmap:core","urn:stalwart:jmap"],"methodCalls":[[METHOD, ARGS, "0"]]}`.
Management objects live under the ADMIN's JMAP accountId (`d333333` in recovery
mode); per-user child objects (PublicKey, AppPassword) are addressed with the
USER's account id instead (= the object id from `x:Account/set`).

1. **Create mailbox** — two calls:
   - `x:Domain/set` create `{"name":"frodo.dotyou.cloud"}` → id
   - `x:Account/set` create `{"@type":"User","name":"frodo","domainId":<domainId>}`
     (`emailAddress` is serverSet = name@domain; DON'T send it).
     Variant discriminator is **`@type`**, always.
   - Main password: `credentials` list of `{"@type":"Password","secret":...}`
     (from Stalwart's own tests; not needed for our flow - clients use app passwords).

2. **Set encryption-at-rest (E2E public cert)** — two calls:
   - `x:PublicKey/set` with **accountId = the user's id**, create
     `{"key":"<ASCII-armored OpenPGP cert>","description":...}` → keyId.
     (`emailAddresses` field rejected our values; omit - not needed.)
   - `x:Account/set` update `{"encryptionAtRest":{"@type":"Aes256","publicKey":keyId,"encryptOnAppend":true,"allowSpamTraining":false}}`.
     Variants: Disabled | Aes128 | Aes256. Verified persisted via read-back.

3. **Install DKIM key** — `x:DkimSignature/set` create:
   `{"@type":"Dkim1Ed25519Sha256","domainId":<id>,"selector":"s1","privateKey":{"@type":"Text","secret":"<PKCS#8 PEM>"}}`
   Variants: Dkim1Ed25519Sha256 | Dkim1RsaSha256 (+ Dkim2* for DKIM2).
   **`publicKey` is serverSet - Stalwart derives it and returns the DNS `p=`
   value on read-back** (`x:DkimSignature/get`) → PR-G's provisioned-DKIM
   drift check is a straight string compare, no crypto needed.

4. **Set aliases** — `x:Account/set` update on the account:
   `{"aliases":{"0":{"name":"mail","domainId":<id>,"enabled":true},"1":{...}}}`
   Registry `List<T>` serializes as a map with **numeric string keys** ("0","1",...)
   - not a JSON array, not name-keyed (verified in crates/registry/src/types/list.rs).
   Alias objects are `{name (localpart), domainId, enabled, description?}`.

5. **Delete mailbox** — `x:Account/set` `{"destroy":[<id>]}` → `destroyed:[id]`.
   (There is also a `x:TaskDestroyAccount` for async teardown; simple destroy
   worked cleanly on a fresh account.)

6. **App password** — `x:AppPassword/set` with **accountId = the user's id**,
   create `{"description":"Thunderbird"}`.
   **`secret` is serverSet: Stalwart GENERATES the password and returns it
   exactly once in the create response** (`app_...` format, its own prefix
   scheme). ⚠️ This inverts PR-F's interface assumption -
   `ProvisionAppPasswordAsync` must RETURN the secret rather than receive one:
   `Task<string> ProvisionAppPasswordAsync(domain, address, label)`.
   (NullMailboxProvider keeps generating its own.)
   Flow consequence for the client (chat-kmp EMAIL_APP.md): the secret is the
   user's mail-client credential and exists exactly once in transit - the KMP
   mini app must save it to the owner-locked email drive when provisioning,
   or the owner can never retrieve it again (only revoke + reissue).

## Revoke and usage — live-verified 2026-08-24

Both were unverified when the provider was first written; probed against the running dev
instance (normal mode) before implementing.

7. **Revoke an app password** — `x:AppPassword/set` with the USER's accountId and
   `{"destroy":["<id>"]}` → `{"destroyed":["<id>"]}`.
   The id comes back from the CREATE response (`created.c1.id`, alongside `secret`)
   — there is no need to list first.
   **Destroying an id the server no longer knows answers
   `{"notDestroyed":{"<id>":{"type":"notFound"}}}`, not an error response.** Since
   `ThrowOnFailedSet` throws on any `notDestroyed` entry, the provider special-cases
   `notFound` as success and rethrows every other reason — which is what makes
   `RevokeAppPasswordAsync` idempotent without a read-then-destroy round trip (and
   without the window that would open between the two calls).
   `x:AppPassword/get` (per-account listing, returns `{id, description}`) exists and
   works, but is deliberately NOT used: the credential list lives on the email drive.

8. **Mailbox usage** — `x:Account/get`, User variant:
   - `usedDiskQuota` — **serverSet**, bytes (`number/size`)
   - `quotas` — mutable map keyed by the `StorageQuota` enum; the disk limit is
     **`maxDiskQuota`** ("Maximum disk space allocated (bytes)"). An absent key means
     unlimited, and a fresh account has `quotas: {}`.
   `GetUsageAsync` never throws — it feeds one line on a status screen, so an
   unanswerable mail server degrades to "not shown".

   Found via `GET /api/schema`, which **302-redirects to a content-addressed path** —
   fetch it with `curl -L` or you get an empty body.

## Error-shape cheatsheet (what the server teaches you)

- `unknownMethod` → wrong method name (it's `x:Type/get|set`, per type).
- `invalidPatch "Missing or invalid '@type'"` → the property is a variant
  object; wrap the value with its `@type`.
- `invalidPatch "Cannot modify server set property"` → drop the field
  (emailAddress, DkimSignature.publicKey, AppPassword.secret).
- `invalidForeignKey {object: Account, id: <adminId>}` on a child create →
  you addressed the admin's JMAP account; use the user's account id.
- User (non-admin) JMAP auth returns **403 in recovery mode** - expected;
  re-test user-facing auth after switching to normal mode.

## Findings the live tests forced back into OUR code (2026-08-21)

Sequoia-pgp (Stalwart's OpenPGP engine) enforces a modern policy that GnuPG
output happens to satisfy and BouncyCastle defaults do not. Both fixed in
`OpenPgpKeyManagement`:

1. **Armor headers**: BC's `ArmoredOutputStream` emits `Version: BCPG C# ...`;
   Stalwart's parser treats header lines as base64 → "Failed to decode base64
   certificate". Fixed by clearing the version header (modern practice anyway).
2. **SHA-1 signatures**: BC's hash-less `PgpKeyRingGenerator` ctor AND the
   3-arg `AddSubKey` overload silently sign with SHA-1; sequoia's
   StandardPolicy discards SHA-1-bound keys → "Could not find any suitable
   keys". Fixed by passing `HashAlgorithmTag.Sha384` in both places.
   Diagnosed by `gpg --list-packets` diff (BC binding sig: digest algo 2 =
   SHA-1; gpg: 9 = SHA-384) after a gpg-made P-384 cert was accepted.

P-384 itself is fully supported (`has_pgp_keys` in
`crates/common/src/storage/encryption.rs` requires a policy-valid,
transport-encryption-capable key - algorithm was never the issue).

## Normal (non-recovery) mode - VERIFIED 2026-08-21

- ✅ Provisioned state (domain/account/DKIM/encryption-at-rest) survives the
  recovery→normal restart intact.
- ✅ User auth with the server-generated app password works against JMAP in
  normal mode (403 in recovery mode is a mode restriction, nothing else) -
  and works WITHOUT any role assignment on the account.
- ✅ STALWART_RECOVERY_ADMIN keeps authenticating in normal mode too - it is a
  standing fallback admin, not recovery-only (the dev instance never creates
  a permanent admin account and doesn't need one).
- 🆕 Stalwart AUTO-CREATES a default per-domain DKIM signature
  (`v1-rsa-<date>` selector) of its own. Our `s1`/`s2` keys coexist with it;
  the provider's delete path sweeps all signatures by domainId so cleanup
  still converges. Flag-flip consideration: decide whether the auto
  signature should be disabled/removed so only Homebase-authored keys sign
  (its selector has no DNS record, so its signatures would fail DKIM anyway).

Still ahead (canary work, not the provider): SMTP acceptance + actual
encryption-at-rest of a delivered message (needs mail-flow ports).
