# ATTESTATION

This is an example of a service that can attest for example your real name, country, etc.

# TODO ATTESTATION

It would be really cool to create an identity, e.g. "attest.odin.earth"
The identity registers it's signing keys with Odin's Key Chain.

We get a copy of the full signature key and place it with this service.

This would make the whole process around it a lot easier for other.
For example, they can look up the identity, they can verify the identity's
current public key, they can call the Key Chain to verify expired public
keys.

@Seb - dockerize it
@ms - identity service secrets (there are none AFAIR...)
@Todd - Create a space for the service on the same server as the Mail API (e.g. "keychain.odin.earth") - @seb reverse proxy?
@Todd - Give Hetzner DNS alias to @ms
@Ms   - Setup CNAME or A in Google for "attest.odin.earth"
@???  - Need to figue out how we store and retrieve the full key (place for secrets)

-- Goal: To do this almost purely in front-end code, with as little, and as generic server code as possible
         The approach outlined here creates just two very small services on the owner API.

@Stef - design UI that let's the user fill in data to attest. Code template is in here (you shouldn't code the signing, see below)
        Call the owner API to get an instruction envelope (Todd will make this call, you just pass the dataToAttest):
           public static SignedEnvelope RequestEnvelope(SortedDictionary<string, object> dataToAtttest)
        Then you have the instruction envelope and send POST it to the keychain.odin.earth API:
           public async Task<ActionResult> PostPublicKeyRegistrationBegin([FromBody] RegistrationBeginModel model)
        Then you get a hash code back that you need to sign (Todd will make this call, you just need to pass the hash):
           public static string SignPreviousHashForPublicKeyChain(string previousHashBase64)
        and send it to finalize (loop N times and resign as long as you get 429):
            public async Task<ActionResult> PostPublicKeyRegistrationComplete([FromBody] RegistrationCompleteModel model)
        Finally, save each signed attribute as a profile attribute.

@Todd - make generic owner only API that essentially exposes this and returns the owner signed instruction envelope
           public static SignedEnvelope RequestEnvelope(SortedDictionary<string, object> dataToAtttest)
        make generic owner only API that signs the keychain hash. Let's discuss how specialized we want it
        (e.g. does it prepend "PublicKeyChain-" to the signature hash? Or is it generic and we rely on Stef doing that 
        (security issue)
           public static string SignPreviousHashForPublicKeyChain(string previousHashBase64)
        We should think about if it's possible to make sure that signing stuff can't happen on the wrong stuff. For example,
        for the keychain I prepend "PublicKeyChain-" to any signature of a hash. This ensures that you can't trick a signature
        for a generic document. 
        
@someone - Someday we should have a job that backs up the blockchain.db



# Odin Attestation Service

## Overview

Odin Attestation Service is an attestation system designed to securely verify and attest to real-world attributes associated
with digital identities. It provides a means of affirming the authenticity and validity of key attributes tied to an individual
or entity's digital presence by means of digital signatures.

## Key Features

- **Real-World Identity Verification**: Odin Attestation Service validates important real-world attributes such as legal names, dates of birth, and other personal credentials tied to a digital identity.

- **Secure Attestations**: Serving as a trusted intermediary, Odin Attestation Service provides attestations that verify the accuracy of a digital identity's real-world credentials. These attestations are secured through cryptographic signatures, providing peace of mind and confidence in their authenticity.

- **Records of Attestation**: Each attestation is recorded and verified at any point in the future. However, the attested data itself resides within the identity, eliminating the risk of data exposure from the Attestation Service.

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


## Technical Notes 

### ECC key storage

The EccKeyStorage class loads and decrypts the full ECC key which is stored encrypted on the disk (more secure than in a DB).
(This is the ECC key we've snatched from id.odin.earth)

### Database Structure


