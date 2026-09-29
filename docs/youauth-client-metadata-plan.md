# YouAuth client metadata: a name the owner can trust, and a pinned callback

Status: plan, 2026-09-27. Implements items 3 and 4 of the YouAuth review that produced PR #1804
(token lifetime) and PR #1817 (error redirects). Decisions taken with the owner of the protocol:
a domain with standing consent re-fetches its document on every login, from cache; and every
Homebase identity serves its own document by default.

## The two problems

**The approval dialog cannot name the site.** When amazon.com asks to authenticate an owner, the
consent page says "amazon.com", a bare domain. The display name the site sends in `client_info` is
discarded, and rightly: anything in the query string was chosen by whoever built the link, so a
phishing page on `arnazon-login.com` could send `client_info=Amazon` and have the dialog print it.
A name fetched from the domain the browser is about to be redirected to is different in one way
only, and it is the way that matters: it is bound to that domain. It does not say the site is honest,
since `arnazon-login.com` can publish a document calling itself "Amazon" just as easily. It says
that the domain which will receive the token calls itself that, so the dialog can show the two
together, and a name that does not fit the domain beneath it is the phishing signal the owner sees.

**The callback is pinned to a host, not a path.** The authorize endpoint checks that the redirect
host equals the client id and accepts any path on it (`YouAuthAuthorizeRequest.ValidateRedirectTarget`).
Fine for amazon.com. On a host where different people own different paths -- GitHub Pages user
sites, a university's `people.example.edu/~name`, any shared host -- anyone with a path can start the
flow in the host's name, get the owner's approval under that name, and receive a token issued to the
whole host.

Neither fix adds registration. A site still meets an identity for the first time with nothing
provisioned on either side; the owner is still met with the approval dialog. What changes is that
the dialog can show the name the redirect domain gives itself, next to that domain, and the domain
can say which path is its callback. The trust is in the domain, never in whoever runs it.

## The document

A site that wants more than a bare domain publishes `https://<client_id>/.well-known/youauth-client.json`:

```json
{
  "name": "Amazon",
  "redirect_uris": ["https://amazon.com/auth/homebase/callback"]
}
```

- Every field is optional. A document with none of them is the same as no document.
- `name`: shown on the dialog and stored as the domain registration's name. Capped at 64 characters,
  control characters stripped, whitespace collapsed. Never shown in place of the domain, only next to it.
- There is no logo, by decision (below): it would add persuasion and no information.
- `redirect_uris`: absolute https URLs on the client's own host. Any other entry is dropped. When the
  list is present and non-empty, the request's `redirect_uri` must match one of them on scheme, host
  and path; the relying party's own query is ignored in the comparison, since it is kept on the way
  back. When the list is absent, the host rule stands.
- No document, a non-200, a wrong content type, a body over 16 KB, or JSON that does not parse all
  mean "no document": today's behaviour, unchanged.

The shape is deliberately the one Bluesky landed on for ATProto OAuth (a client metadata document
at a URL the client controls), so a site that already publishes one for that ecosystem has nothing
new to learn. The trust model stays "what the redirect domain serves about itself": the identity
trusts that the document came from that domain, and nothing more.

## What the identity does with it

**Fetch, at step [030] after the trust checks.** `YouAuthClientMetadataService` in Odin.Services,
one method: `GetAsync(AsciiDomainName clientId)` returning `YouAuthClientMetadata` (name,
redirect URIs, all nullable) or an empty instance. It fetches through `IOdinHttpClientFactory` with
a typed Refit client, which is how every other call to a peer's host is made and what the V2 test
host routes in-process. Short timeout (3 s), redirects not followed, response size capped before
parsing, strict parser that drops fields rather than failing the whole document.

The fetch is only ever made for a client id that is a domain client whose redirect host has
already passed `ValidateRedirectTarget`, so the identity never fetches from a host nobody asked
it to. `localhost` is exempt from the fetch as it is from the host rule.

**Cache, in the tenant's level-2 cache, per client id.** One hour when a document was found; five
minutes when it was absent or malformed. A site's outage or a typo therefore cannot stall logins,
and a renamed site or a newly published `redirect_uris` takes effect within the hour without the
owner revoking anything. This is the "re-fetch on every login" decision: every login reads the
cache, the cache reads the site.

**Pin the callback.** `ValidateRedirectTarget` gains an optional set of allowed redirect URIs. With
the set present, a request whose redirect URI is not in it is refused with a 400, the same answer as
a host mismatch, because the site itself said that path is not its callback and the target is
therefore not trusted. `YouAuthAuthorizeRequest` stays a query-binding DTO; the controller passes
the set in.

**Carry the verified name.** The app path already overwrites `client_info` with the app's friendly
name for the token it issues. The domain path overwrites it with the document's name and, unlike
the app path, sends the request as validated to the consent page, so the page sees it. Two
consumers pick it up: the consent page reads it from the return URL, and `YouAuthUnifiedService`
stores it as the registration's `Name` and the client's friendly name where today it stores the
bare domain, so the owner's list of connected domains says "Amazon".

**Identities publish one too.** An anonymous endpoint next to the WebFinger and DID controllers
serves the identity's own document: the owner's display name from the public profile and the public
image the consent page already fetches. No callback list: an identity host has exactly one relying
party on it, itself, so a pin there protects nothing, and it would force every peer to match a port
the identity cannot know (the test host, for one, echoes the request's port into redirects). Every
Homebase identity is thereby a relying party that names itself, and a peer signing in on another's
home site sees "Sam Gamgee" rather than a host. This is the "on by default" decision.

## The consent page, in odin-js

The dialog answers one question first: where is this login going? The domain leads, large and
painted by `DomainHighlighter`, exactly as today. Everything the redirect domain says about itself is
subordinate to that, in size and in wording, so the owner is not led cognitively by a claim.

- **Lead:** the redirect domain, large, through `DomainHighlighter`. Unchanged.
- **Below it, small:** "Calling itself 'Amazon'", only when the server passed a name in `client_info`.
  The same highlighter on the name, so a homoglyph inside the quotes is painted too. The phrasing does
  the work: it says this is the domain's claim, not a fact the identity checked.
- **No logo.** Dropped from the document, the fetch and the redirect by decision: a logo is the most
  persuasive and least verifiable thing a domain can publish, since any site can serve another's. The
  page keeps its small favicon fetch, which the owner reads as decoration, and nothing larger.
- **Cancel** keeps the relying party's `state` and query, on this page and on the app-registration
  page. It strips the whole query today, the follow-up noted on PR #1817.

## Tests, red first

All in Odin.Hosting.Tests.V2, the fixture style of `YouAuthErrorRedirectTests`, step numbers in
the names per `docs/youauth-unified-authorization.md`.

**Parser and fetcher**, against a canned response, in `YouAuthClientMetadataTests`:

- absent (404) gives an empty document and is cached as such;
- a valid document round-trips name and redirect URIs; a logo, if still published, is ignored;
- a body over the size cap, a non-JSON body and a wrong content type each give an empty document;
- a redirect URI on a foreign host and an http one are each dropped while the rest of the document
  survives;
- a name with control characters and runs of whitespace comes back cleaned and capped.

**Integration**, Sam as the relying party because his identity now serves a document:

- `YouAuth030_AVerifiedNameReachesTheConsentPage`: Frodo authorizing a login from Sam is redirected
  to consent with `client_info` equal to Sam's display name.
- `YouAuth055_TheVerifiedNameIsStoredOnTheRegistration`: after consent, Frodo's registration for
  Sam's domain carries that name, and the connected-domains list shows it.
- `YouAuth030_ARedirectPathTheDomainDidNotPublishIsRefused`: a redirect URI on a made-up site's host
  but not in its canned `redirect_uris` gets a 400. The test host's fetch factory serves canned
  documents for made-up hosts, since identities publish no callback list.
- `YouAuth030_TheRelyingPartysQueryDoesNotBreakThePin`: that site's published callback plus
  `?session=abc` is accepted.
- `YouAuth030_ADomainWithNoDocumentBehavesAsToday`: a throwaway domain gets the host rule and the
  bare domain on the consent redirect.

**Own document**: `GET /.well-known/youauth-client.json` on Sam, anonymously, returns his name and
no callback list; an identity with no public profile still returns a valid, empty document.

## Order of work

1. Tests, committed red.
2. The parser and fetcher, with the cache.
3. The well-known endpoint.
4. The controller and service changes.
5. Simplify pass, `SIMPLIFY:` prefixed.
6. The odin-js PR against the consent and app-registration pages, which can land independently:
   without it the server change is invisible but harmless, since the page ignores `client_info` for
   domains today.
7. Update `docs/youauth-unified-authorization.md` step [030] and the prose for the document, in the
   same PR as the server change.

## Not in this PR

- Rejecting a site that publishes no document. The fallback is the whole point of no registration.
- Signing the document or serving it over DNS.
- Using the document to pre-fill the permission request. `permission_request` is still the
  request's, not the site's.
- The LSD draft's `/.well-known/youauth` for identity-host metadata, which is a different document
  about the other party.
