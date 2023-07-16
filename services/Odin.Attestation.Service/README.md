# TODO

It would be really cool to create an identity, e.g. "id.odin.earth"
The identity registers it's signing keys with Odin's Key Chain

We get a copy of the full signature key and place it with this service.

This would make the whole process around it a lot easier for other.
For example, they can look up the identity, they can verify the identity's
current public key, they can call the Key Chain to verify expired public
keys.


# Odin Attestation Service

## Overview

Odin Attestation Service is a blockchain enabled attestation system designed to securely verify and attest to real-world 
attributes associated with digital identities. Leveraging the power of Elliptic Curve Cryptography (ECC) and blockchain
technology, it provides a means of affirming the authenticity and validity of key attributes tied to an individual or entity's
digital presence.

## Key Features

- **Real-World Identity Verification**: Odin Attestation Service validates important real-world attributes such as legal names, dates of birth, and other personal credentials tied to a digital identity.

- **Secure Attestations**: Serving as a trusted intermediary, Odin Attestation Service provides attestations that verify the accuracy of a digital identity's real-world credentials. These attestations are secured through cryptographic signatures, providing peace of mind and confidence in their authenticity.

- **Immutable Records of Attestation**: Each attestation is permanently recorded using blockchain technology. This results in an unalterable record that can be trusted and verified at any point in the future. However, the attested data itself resides within the identity, eliminating the risk of data exposure from the Attestation Service.

- **Interoperability with Odin's Key Chain**: Odin Attestation Service is designed to work seamlessly with Odin's Key Chain, a secure service for the registration and management of digital identities.

- **Third-Party Verification**: While each attestation can be independently verified using public keys, in the unlikely event of the underlying ECC being compromised, third parties can contact the Attestation Service to verify the nonce of signed objects.

> Note: Odin Attestation Service is a work in progress. Your feedback and contributions are highly welcome!


## Attestation JSON Structure
Here's an outline of the Attestation JSON structure:

{
    "Envelope": {
        "ContentHash": "<SHA-256 hash of the content>",
        "Nonce": "<Unique nonce for the attestation>",
        "ContentHashAlgorithm": "<Hash algorithm used, e.g. 'SHA-256'>",
        "ContentType": "<Type of content, e.g. 'attestation'>",
        "TimeStamp": <UNIX timestamp>,
        "Length": <Length of the content>,
        "AdditionalInfo": {
            "attestationFormat": "<Format of attestation, e.g. 'personalInfo'>",
            "authority": "<Authority issuing the attestation, e.g. 'id.odin.earth'>",
            "data": {
                "LegalName": "<Legal name of the identity>"
            },
            "expiration": "<Expiration date of the attestation>",
            "identity": "<Identity being attested, e.g. 'frodo.baggins.me'> ($signature is to be replaced with the signature value below)",
            "issued": "<Issued date of the attestation>",
            "URL": "<URL for verification>",
            "usagePolicyUrl": "<URL of the usage policy>"
        }
    },
    "Signatures": [
        {
            "DataHash": "<SHA-256 hash of the data>",
            "DataHashAlgorithm": "<Hash algorithm used, e.g. 'SHA-256'>",
            "Identity": "<Identity of the signing authority, e.g. 'id.odin.earth'>",
            "PublicKeyDer": "<Public key of the signing authority>",
            "TimeStamp": <UNIX timestamp>,
            "SignatureAlgorithm": "<Signature algorithm used, e.g. 'SHA-384withECDSA'>",
            "Signature": "<Signature of the signing authority>"
        }
    ]
}

In the Signatures array, each object represents a signature provided by a signing authority.
Each signature includes a DataHash which is the hash of the data being signed, the DataHashAlgorithm used,
the Identity of the signing authority, their public key (PublicKeyDer), a timestamp of when the signature 
was made, the SignatureAlgorithm used, and the Signature itself.

## Web Services

==== PROJECT 1:

### VerifyAttestation(string signature)
  - Returns HTTP 200 if the signature exists and is valid (yes this service signed the attestation).
  - Returns Not Found if the nonce doesn't exist (this service did not sign the attestation). 
  - Returns ??? and a timestamp of the revokation if the nonce has been revoked (yes this service signed the attestation but it has been revoked as is not valid).

### RequestAttestation(string identity, string requestCode)
An identity registers an attestation request with this service. The request will linger 
until processed by a human on the attestation team. Once processed the row
is deleted.

Note:
  To verify the public key used in an attestation, you'd check with Odin's Key Chain.

===
QUESTION... DO I MAKE TWO PROJECTS? IF SO HOW DO I GET 


PROJECT 2:

DeleteAttestation(string identity, string requestCode)

!!! Everything below here is a non-public service running on a different server which has no open inbound ports. !!!

### AttestHuman(string identity)

### AttestLegalName(string identity, string )

## Technical Notes 

### ECC key storage

The EccKeyStorage class loads and decrypts the full ECC key which is stored encrypted on the disk (more secure than in a DB).
(This is the ECC key we've snatched from id.odin.earth)

### Database Structure

Table: Requests

- `identity` (STRING): The identity requesting an attestation
- `requestCode` (STRING): A code for the request


Table: Attestation

Each row in this immutable blockchain includes the following fields:

- `previousHash` (BLOB): A unique SHA-256 hash of the previous row.
- `timestamp` (INT): The timestamp when this record was created.
- `nonce` (BLOB): The unique nonce value from the envelope of the attested information.
- `signedNonce` (BLOB): The nonce signed by the registering entity (solely to make the block chain secure).
- `publicKey` (BLOB): The public key of the identity signing the nonce (used to verify the chain)
- `algorithm` (STRING): The cryptographic algorithm used for signing.
- `recordHash` (BLOB): A unique hash value of this record.

Table: AttestationRevokations

Each row in this table includes the nonce of an attestation that has been revoked

- `nonce` (BLOB): The unique nonce value from the envelope of the attested information.
- `timestamp` (INT): The timestamp when this record was created.
- `algorithm` (STRING): The cryptographic algorithm used for signing.

-----TODO

DECIDE IF IT'S A BLOCKCHAIN OR NOT
IF IT IS, THEN DO WE NEED A (REMOTE) SIGNATURE TO ENSURE IMMUTABILITY OF EACH ROW? (ON DELIVERY OF THE DATA)
THEN DO WE NEED A SEPARATE REVOKATION TABLE?
