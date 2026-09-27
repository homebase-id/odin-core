# YouAuth unified flow

```mermaid
sequenceDiagram
    participant user as Sam
    participant client as Sam's browser
    participant frodosBackend as Program seeking to get proof of Sam's identity<br/> (third-party must implement)
    participant samsBackendAuthorize as https://samwise.me/api/youauth/authorize<br/> BACKEND
    participant samsFrontend as https://samwise.me/owner<br/> FRONTEND
    participant samsBackendToken as https://samwise.me/api/youauth/token<br/> BACKEND

    user->>client: browse

    note over client, client: Sam types his Odin ID sam.me<br/>and clicks login

    frodosBackend->>frodosBackend: [010] Generate or use ECC private/public key pair<br/>

    client->>samsBackendAuthorize: [030] Request authorization code<br/> Browser: GET /authorize?<br/> client_id = uuid_or_domain<br/> client_type = app_or_domain<br/> client_info = json_or_url_with_client_info<br/> permission_request = list_of_permissions_to_consent_to<br/> public_key = public_key_from_previous_step<br/> state = user defined value<br/> redirect_uri = callback_url_with_authorize_response

    samsBackendAuthorize-->>samsFrontend: [040] Logged in?<br/>If Sam is not logged in (no owner-cookie), redirect (302) to<br/> https://samwise.me/owner/login?returnUrl=<current uri>

    samsFrontend-->>samsBackendAuthorize: [042] Log in and redirect back<br/> Sam logs in and the browser is redirected back to 'returnUrl'

    samsBackendAuthorize-->>samsFrontend: [045] client_type is app?<br/> If app is not registered at Sam's identity, redirect (302) to<br/> https://samwise.me/owner/appreg?returnUrl=<current uri>

    samsFrontend-->>samsBackendAuthorize: [047] Approve app registration<br/> Sam is shown approval screen. If Sam clicks OK,<br/> app is registered.<br/> Redirect browser back to 'returnUrl'

    samsBackendAuthorize-->>samsFrontend: [050] Consent needed?<br/> If backend determines that Sam needs to consent, redirect (302) to<br/> https://samwise.me/owner/youauth/authorize?<br/> client_id = uuid_or_domain<br/> client_info =  json_or_url_with_client_info<br/> permission_request = list_of_permissions_to_consent_to<br/> returnUrl = <current uri>

    samsFrontend-->>samsBackendAuthorize: [055] Give consent and redirect back<br/> Sam is shown consent screen. If Sam clicks OK,<br/> consent to/for client_id and permission_request are stored in database<br/> by POST'ing to /authorize.<br/> Finally redirect browser back to 'returnUrl'

    samsBackendAuthorize-->>samsFrontend: [060] Validate consent and scopes.<br/> Show error if something is wrong.

    samsBackendAuthorize->>samsBackendAuthorize: [070] Create ECC private/public key pair, random salt<br/> and shared secret based on public_key from step 30.<br/> Create client access token and store it encrypted<br/> with shared secret in cache for later lookup.

    note over client, samsBackendAuthorize: (below: redirect because native app must be able to intercept it)

    samsBackendAuthorize->>frodosBackend: [080] Return identity host's public key and salt.<br/> Redirect (http 302) to redirect_uri<br/> state = user defined value from above<br/> public_key = public key from above<br/> salt = random salt from above<br/> identity = authorizing identity

    frodosBackend->>frodosBackend: [090] Calculate shared secret and digtest for token exchange<br/> based on private key from step 10 and public key<br/> and salt from previous step.

    frodosBackend->>samsBackendToken: [100] Request exchange digest of shared secret for access token<br /> POST /token?<br/> secret_digest = SHA256 digest of shared secret

    samsBackendToken->>samsBackendToken: [110] Load encrypted client access token<br/> from cache based on shared secret<br/>

    samsBackendToken-->>frodosBackend: [120] Return 404 if lookup failed

    samsBackendToken->>frodosBackend: [140] Return encrypted cat and new shared secret<br/> Return(http 200)

    frodosBackend->>frodosBackend: [150] Decrypt token with ECC exchange shared secret

    note over frodosBackend: Authentication and authorization now complete. Program now has proof of Sam's identity.

    frodosBackend->>frodosBackend: [400] Post-YouAuth for homepage login:<br/> fetch remote half key in order to unlock Sam's key-box on Frodo's homepage.<br/> Then we can generate CAT from Frodo's server to Sam's browser. Set the cookies.

```

---

The purpose of YouAuth is to:

- Obtain user consent for a `program` to access certain data.
- Return a machine readable token to the `program` facilitating said access.

`program` can be one of:

- `app` (single page or native app - e.g. photos.odin.earth)
- `domain` (e.g. frodo.me or shop.amazon.com)

## `authorize` endpoint

`GET` query parameters for the `authorize` endpoint:

- `redirect_uri` (required): The callback uri where the authorization parameters are delivered (see below).
- `client_type` (required): This parameter will help determine what kind of permissions go into the final Client Access Token (CAT). It can be one of the following values:
  - `app`: The client will use this value when it is an app. E.g. Sam logging on to photos app on his phone.
    - consent: An app always requires consent from the user.
  - `domain`: The client will use this value when it is a domain. E.g. Sam using his identity to log on to amazon.com or frodo.me/home.
    - consent: Consent will be necessitated for any domain unless Sam has previously granted approval for automatic consent on that specific domain. If `permission_request` is set, and is requesting permissions not already granted, then a consent is always required.
- `client_id` (required): unique identifier of the client. The value requirement depends on the `client_type`:
  - `app`: The value is required and should be a uuid.
  - `domain`: The value must match the callback domain in the `redirect_uri`.
- `public_key` (required): Base64 encoded ECC public key.
- `state` (optional): A value included in the request that is also returned in the authorization callback response/redirect.
- `permission_request` (optional): (tbd) list of explicit permissions the CLIENT is requesting. E.g. "drive:xyz:read drive:xyz:write".
- `client_info` (optional): (tbd) JSON structure with CLIENT details (e.g. name, description, list of scopes, signature, etc.) shown on consent screen. Alternatively a URL from which the json structure can be fetched.

The response to the `authorize` endpoint is delivered using `HTTP 302 Redirect` to `redirect_uri` parameter with the following query parameters:

- `identity`: The identity id.
- `public_key`: The identity host's public key (required below).
- `salt`: The identity host's random salt (required below).
- `state`: Copy of the user defined state data.

## `token` endpoint

`POST` body parameters for the `token` endpoint:

- `secret_digest`: SHA256 digest of the ECC shared secret calculated from {client private key from step 10, identity host public key from step 80, identity host random salt from step 80}. It must match the digest created on the identity host during `authorize`.

The response to the `token` endpoint is a JSON object with the following members:

- `base64SharedSecretCipher`: The AES CBC encrypted new shared secret. Use base64SharedSecretIv (below) and shared secret from step 90 to decrypt.
- `base64SharedSecretIv`: (see above)
- `base64ClientAuthTokenCipher`: The AES CBC encrypted CAT. Use base64ClientAuthTokenIv (below) and shared secret from step 90 to decrypt.
- `base64ClientAuthTokenIv`: (see above)

## `Shared secret` calculation
Both the identity host and the third-party site compute the shared secret independently using their private keys and the other party's public key, combined with a random salt provided by the identity host. The formula for the shared secret could be expressed as:

    Shared Secret = HKDF(ECDH(private_key_self, public_key_other_party), salt)

This ensures that both parties derive the same shared secret without transmitting it over the network. Using a KDF strengthens the shared secret by ensuring it's uniformly random and resistant to attacks. It also allows the incorporation of the salt in a cryptographically secure manner.
