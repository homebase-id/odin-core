# TODO NOTARIUS

# Notarius Publicus Blockchain Service for notarizing signed documents

This service inspects and validates all the signatures of a signed document, and if everything checks
out then it signs the signed envelope and records it's signature on a public blockchain. 
The end result is that a party can later validate the notarized document.

## Web Services

### Verify(string notarySignatureBase64)

### PostPublicKeyNotaryBegin([FromBody] NotaryBeginModel model)
  model.RequestorIdentity is the identity of the identity making the request
  model.SignedEnvelopeJson is the signed envelope to notarize

  Validates the validity of the envelope and it's signatures.
  Validates the requesting identity has a public key
  Stores the request in a memory cache
  Returns the previousHash to the requestor to sign


### PostPublicKeyNotaryComplete([FromBody] NotaryCompleteModel model)
  Receives a properly signed previousHash or fails.
  If too slow and not the last hash, then return a new value the requestor must sign
  If ok, notarize the document, add to the blockchain and return the notarized document

## Database
Each row in the blockchain includes the following fields:

- `previousHash` (BLOB): A unique SHA-256 hash of the previous row.
- `identity` (STRING): The identifier of the entity requesting a document to be notarized
- `timestamp` (INT): The timestamp when the record was created.
- `signedPreviousHash` (BLOB): The previousHash signed by the requestor (making the blockchain immutable).
- `algorithm` (STRING): The cryptographic algorithm used for signing.
- `publicKeyJwkBase64Url` (STRING): The registering entity's public signature key in the JWK format, base64url encoded.
- `notarySignature` (BLOB): The (unique) signature of Notarius Publicus
- `recordHash` (BLOB): A unique hash value of this record.

## Security Features

## Future Developments
The next significant step for this project is to turn it into a distributed system, where multiple copies of the blockchain can be maintained across various nodes.
This would provide additional security, transparency, and reliability for the registered data, thereby elevating the service to the next level.

Contributions and suggestions towards this goal, or any other improvements, are warmly welcome. Enjoy using and improving this basic blockchain service!
