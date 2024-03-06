# TODO KEY-CHAIN 

@Seb - dockerize it
@ms - identity service secrets (there are none AFAIR...)
@Todd - Create a space for the service on the same server as the Mail API (e.g. "keychain.odin.earth") - @seb reverse proxy?
@Todd - Give Hetzner DNS alias to @ms
@Ms   - Setup CNAME or A in Google for "keychain.odin.earth"

-- Goal: To do this almost purely in front-end code, with as little, and as generic server code as possible
         The approach outlined here creates just two very small services on the owner API.

@Todd/@Stef - Create something we can use for a "red circle" in the owner console if current key not registered?
              Create a red circle if the ECC signature key is rotated (punt for now?)
              Add a red circle / green checkmark in some kind of status overview where you can see your signature key. 
              Maybe you can even get your public key there.
@Stef - Add a button to register the signature key (for when key is rotated or if it failed in the initial wizard, aka when the red circle is there)

@someone - Someday we should have a job that backs up the blockchain.db

# Basic Blockchain Service for Public Key Registration

This project is a simple yet robust blockchain service designed to securely register and store identities' public signature keys.
Each row in the blockchain consists of several crucial elements that help guarantee the integrity and authenticity of the stored data.

## Web Services

### Verify(string identity)
Returns HTTP 200 and a JSON of the oldest registration timestamp of the provided identity (aka identity age), otherwise returns "Not Found". 

### VerifyKey(string identity, string publicKeyDerBase64)
Returns HTTP 200 and a JSON of the public key registration timestamp range of the provided identity, otherwise returns Not Found. 
If the public key is still the currently last registered key then there is no end-range.

### PostPublicKeyRegistrationBegin([FromBody] RegistrationBeginModel model)
POST HTTP call, the 'model' is simply a JSON of an intruction envelope requesting the inclusion of the public key
Begins the process of registering a public key. Returns the previousHash value that needs to be signed.

### PostPublicKeyRegistrationComplete([FromBody] RegistrationCompleteModel model)
POST HTTP call, the 'model' the EnvelopeIdBase64 string from the RegistrationBegin request and the SignedPreviousHashBase64 from the requestor
Completes the process of registering a public key.

## Database
Each row in the blockchain includes the following fields:

- `previousHash` (BLOB): A unique SHA-256 hash of the previous row.
- `identity` (STRING): The identifier of the entity registering its public key.
- `timestamp` (INT): The timestamp when the record was created.
- `signedPreviousHash` (BLOB): The previousHash signed by the requestor (making the blockchain immutable).
- `algorithm` (STRING): The cryptographic algorithm used for signing.
- `publicKeyJwkBase64Url` (BLOB): The registering entity's public signature key in the JWK format, base64url encoded.
- `recordHash` (BLOB): A unique hash value of this record.

## Security Features
The main security feature of this blockchain service is the `signedPreviousHash` field. This value is the hash from the previous row signed by the entity registering its public key, which serves two essential purposes:

1. **Authentication**: The signed previousHash makes the blockchain immutable and proves the identity of the entity making the request, as only the holder of the private key can produce a valid signature.

2. **Integrity**: The blockchain's integrity is maintained as any modification to the blockchain would require all subsequent `signedPreviousHash` values to be recalculated. Given that these signatures can only be produced by the respective identity, it effectively safeguards the blockchain against unauthorized modifications.

## Future Developments
The next significant step for this project is to turn it into a distributed system, where multiple copies of the blockchain can be maintained across various nodes. This would provide additional security, transparency, and reliability for the registered data, thereby elevating the service to the next level.

Contributions and suggestions towards this goal, or any other improvements, are warmly welcome. Enjoy using and improving this basic blockchain service!
