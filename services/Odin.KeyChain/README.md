# KEY-CHAIN TODO

@Todd - Create a space for the service on the same server as the Mail API ("keychain.odin.earth") - @seb reverse proxy?
@Todd - Give alias to @ms
@Ms   - Setup CNAME or A in Google for "keychain.odin.earth"
@Todd - create the service that triggers registration, see code prepped for you.
@Todd - Create something we can use for a "red circle" if not registered.
@Todd - Create a red circle if the ECC signature key is rotated (punt for now?)
@Stef - Add a checkbox step to the initial wizard to register the signing key (checked by default)
@Stef - Add a red circle in the owner console if registration is missing
@Stef - Add a button to register the signature key (for when key is rotated or if it failed in the initial wizard)
@Stef - Add a red circle / green checkmark in some kind of status overview where you can see
        your signature key. Maybe you can even get your public key there.

@someone - Someday we should have a job that backs up the blockchain.db

# Basic Blockchain Service for Public Key Registration

This project is a simple yet robust blockchain service designed to securely register and store identities' public signature keys. Each row in the blockchain consists of several crucial elements that help guarantee the integrity and authenticity of the stored data.

## Data Structure

Each row in the blockchain includes the following fields:

- `previousHash` (BLOB): A unique SHA-256 hash of the previous row.
- `identity` (STRING): The identifier of the entity registering its public key.
- `timestamp` (INT): The timestamp when the record was created.
- `nonce` (BLOB): A unique nonce value.
- `signedNonce` (BLOB): The nonce signed by the registering entity.
- `algorithm` (STRING): The cryptographic algorithm used for signing.
- `publicKey` (BLOB): The registering entity's public signature key.
- `recordHash` (BLOB): A unique hash value of this record.

## Security Features

The main security feature of this blockchain service is the `signedNonce` field. This value is the nonce signed by the entity registering its public key, which serves two essential purposes:

1. **Authentication**: The signed nonce proves the identity of the entity making the request, as only the holder of the private key can produce a valid signature.

2. **Integrity**: The blockchain's integrity is maintained as any modification to the blockchain would require all subsequent `signedNonce` values to be recalculated. Given that these signatures can only be produced by the respective identity, it effectively safeguards the blockchain against unauthorized modifications.

## Future Developments

The next significant step for this project is to turn it into a distributed system, where multiple copies of the blockchain can be maintained across various nodes. This would provide additional security, transparency, and reliability for the registered data, thereby elevating the service to the next level.

Contributions and suggestions towards this goal, or any other improvements, are warmly welcome. Enjoy using and improving this basic blockchain service!
