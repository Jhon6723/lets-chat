# ADR 0007: Authentication and identity providers

- Status: Accepted
- Date: 2026-09-19

## Context

The relay must authenticate three distinct things, and they are not the same:

1. **Social identity** — who this user is (username, contacts directory, profile). Needs recovery UX and human-meaningful handles.
2. **Device identity** — which device is talking. The device already holds a Signal identity keypair generated locally (ADR 0006): the private key never leaves the device.
3. **Sensitive relay operations** — fetching a mailbox, publishing prekeys, registering a device. If anyone can do these with just an address string, the relay leaks metadata, accepts injected prekey bundles (MITM), and is trivially DoS-able.

Constraints from the product position:

- The app's two differentiators are **total privacy** and **good translation via external providers that are strictly opt-in/BYOK**. Translation is the only sanctioned third-party data path — nothing else may leak to external services by default.
- RNF-05: the PWA ships a strict CSP and minimal third-party scripts, because XSS is the primary E2EE threat in a browser runtime. OAuth SDKs, redirect flows, and iframes enlarge exactly that surface.
- ADR 0005: the vault passphrase is local-only and must never be entangled with server credentials.
- ADR 0006: devices already possess an identity keypair suitable for signatures (TOFU / identity-change detection already demonstrated in spike-01).

## Options considered

### Option A: Passwordless, device-signature only

Server issues a random challenge; the device signs it with its Signal identity private key; the server verifies against the published public key. Registration = choosing a username bound to the identity public key.

- Pros: nothing to brute-force or leak, the server can never impersonate a user, zero third parties, smallest auth code surface.
- Cons: losing the device loses the account until the encrypted backup/seed flow lands (ADR 0005 post-MVP); no familiar credential recovery UX at MVP.

### Option B: Classic username/password + JWT

- Pros: familiar, easy email-based recovery.
- Cons: credential DB to protect; whoever controls the account can register devices and MITM mailboxes unless safety-number UX exists (deferred to hardening per ADR 0006); interpreted alone it weakens the E2EE promise.

### Option C: Hybrid — self-hosted credentials for social identity, device signature for sensitive operations

Account = username + password (Argon2id) handles social identity (username claim, contacts, profile). Every sensitive relay operation additionally requires a device-signature challenge against the device's Signal identity key.

- Pros: familiar account UX and recovery; compromising the password alone does NOT unlock the mailbox or permit prekey injection — the attacker would have to register a fresh device, which contacts see as a new identity (TOFU story from ADR 0006); no third party in the login path.
- Cons: two mechanisms to build and reason about; users may conflate the account password with the vault passphrase — the UX must keep them visibly distinct.

### Option D: External IdP (OAuth / Auth0 / Firebase) + device signature

- Pros: zero credential management, delegation of recovery, familiar social login.
- Cons: the provider observes every login (who, when, IP — metadata leak against the privacy pitch); enlarges the PWA third-party/XSS surface (RNF-05); lock-in and free-tier limits; and it still does not authenticate the device, so the Signal signature layer has to be built anyway — option D adds a dependency without removing the hard part.

## Decision

**Option C: self-hosted username/password account (Argon2id-hashed) for social identity, plus device-signature challenge–response for every sensitive relay operation. No external identity provider in the MVP.**

Concretely:

- Registration: username + password; server stores Argon2id hash. This creates the social identity.
- Device binding: registering a device publishes its Signal identity public key under the account; the registration request itself must be signed by the device key.
- Sensitive operations (fetch pending envelopes, publish prekeys, send): require a short-lived challenge signature from the device. The WS relay connection is authenticated this way.
- The account password and the vault passphrase (ADR 0005) are separate secrets: the password never derives encryption keys, and the passphrase never leaves the device.
- External IdPs (Option D) remain a post-MVP candidate strictly as optional social linking/discovery, never as the only auth path.

### Session strategy (token mechanics)

JWT answers a different question than the auth model — how to avoid re-authenticating on every request. The strategy minimizes what a stolen credential can do:

- **Device scope has no stealable token.** The WS relay connection authenticates per connection: the server sends a single-use random nonce on connect; the app signs it with the device identity key; the connection is authenticated. Nothing with mailbox power persists in browser storage — reconnecting requires a fresh signature against a fresh nonce.
- **Account scope uses short-lived JWT.** Login issues an access token (~15 min) for social endpoints (profile, contacts) plus a refresh token with rotation — each use issues a new one and invalidates the previous; reuse of an old refresh token invalidates the whole family (theft signal). Worst-case theft grants minutes of social endpoints, never the mailbox or prekeys.
- **XSS is the residual root threat.** Any attacker running script in the PWA origin can ask the SDK to sign challenges live; no token design fixes that. Structural defense is RNF-05 (strict CSP, no third-party scripts, pinned dependencies).

## Consequences

### Positive

- Auth path has zero third-party observers — consistent with the product's privacy baseline; the only external calls in the whole system remain the user's own consented BYOK translation requests.
- Password compromise is insufficient for mailbox access or prekey injection; device operations are cryptographically tied to the device key already managed by the signal SDK.
- Familiar registration UX for users without sacrificing the can't-impersonate property at the transport layer.
- No OAuth libraries or redirects in the PWA; the CSP stays tight (RNF-05).

### Negative and mitigations

- **Two auth mechanisms to maintain.** Mitigation: the challenge-sign flow is a single endpoint pair (issue challenge, verify signature) built on the identity key the SDK already manages; the password flow is ordinary.
- **Password vault still exists.** A credential DB is a target. Mitigation: Argon2id with strong parameters, no password reuse with the vault passphrase (enforced by keeping them textually distinct flows), rate limiting on login.
- **Device loss recovery gap.** Until encrypted cloud backup lands, a lost device means re-registration and identity-change warnings for contacts. Mitigation: documented as part of ADR 0005's backup milestone; MVP accepts it.

### Revisit triggers

Re-evaluate if: users demand social login despite the privacy trade-off (introduce IdP as optional linking only); the dual-mechanism UX proves confusing in usability testing (consider merging toward pure Option A once backup/recovery exists); or regulatory/hosting context changes.
