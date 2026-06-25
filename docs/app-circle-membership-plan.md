# Plan: Allow apps to add OdinIds to a circle

## Problem statement

Currently you must have the master key in order to add an OdinId to a circle. We
need to change this so that an app can add OdinIds into a circle.

This is blocked at four independent layers — two are policy (who is allowed) and
two are cryptographic (the app physically lacks the keys):

1. **The auth gate refuses the app outright.** `GrantCircleAsync` (and
   `RevokeCircleAccessAsync`) call `AssertHasMasterKey()` as their first line. An
   app context is built with `masterKey: null`, so the call throws before
   anything else runs. This is the visible blocker.

2. **There is no permission an app could present instead.** App operations are
   authorized by named permission keys the app was granted (e.g.
   `ReadConnections`, `UseTransitWrite`), checked via `HasPermission(...)`. The
   add-to-circle path has no such check — its only authorization is the master
   key. The circle permission-key set only contains `ReadCircleMembership`
   (read-only); there is no "manage circle membership" key, and nothing in the
   app-allowed permission set maps to this operation. So even if the gate in #1
   were removed, the system would have no token of authority to distinguish an
   allowed app from a disallowed one.

3. **The app cannot decrypt the connection's keyStoreKey.** Building a grant
   starts by decrypting the target connection's keyStoreKey, which is stored
   only as `AccessExchangeGrant.MasterKeyEncryptedKeyStoreKey`. An app holds the
   ICR key, not the master key, and there is no app-accessible copy of that
   keyStoreKey on a normal connection. The app cannot even begin to assemble the
   grant.

4. **The app cannot decrypt the drive storage keys.** A circle grant that
   includes drive reads must re-encrypt each drive's storage key for the new
   member. The storage key exists only as `StorageDrive.MasterKeyEncryptedStorageKey`,
   with no app-accessible copy. Without the master key this silently produces a
   grant with no storage key — a member who is "in the circle" but cannot read
   any of its drives.

Blockers #1 and #2 are policy. Blockers #3 and #4 are cryptographic and are why
this is not a one-line auth change: the alternative-key-encrypted copies of these
secrets do not exist yet and must be created for existing data (see below).

## Challenge: we need to upgrade data

The master key is not just an auth gate — it is the only key that can currently
decrypt the secrets needed to build a working circle grant:

- A connection's keyStoreKey is stored only as
  `AccessExchangeGrant.MasterKeyEncryptedKeyStoreKey`.
- A drive's storage key is stored only as `StorageDrive.MasterKeyEncryptedStorageKey`.

There is no app-accessible (e.g. ICR-key / online-key) copy of either today. So
enabling an app to grant a circle requires upgrading existing data to add the
alternative-key-encrypted copies of these secrets (for existing connections and
existing drives), not just changing the code path.
