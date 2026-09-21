# Security model — layered defense

How Let's Chat defends encrypted messaging in a hostile world: what we protect, against whom, and with which layer. The individual decisions live in the ADRs; this document is the map that connects them.

## Baseline promise

The server operator — including us — can never read message content. External services only receive data the user explicitly consented to send (translation, BYOK). Everything else is defended in layers, ordered from the user inward:

```
Layer 0  User judgment        → verification UX, consent flows
Layer 1  Content crypto       → Signal protocol (E2EE)
Layer 2  Local storage        → passphrase-derived vault encryption
Layer 3  Device auth          → per-connection signature challenge
Layer 4  Account auth         → short-lived JWT, credential hygiene
Layer 5  Browser surface      → strict CSP, no third-party scripts
Layer 6  Server posture       → dumb relay, nothing sensitive persisted in plaintext
```

## Layer 0 — User judgment

The last line of defense and the first to fail if the UX trains users to click through warnings.

- Translation is always explicit opt-in (global toggle OFF + per-message consent sheet), with provider name and jurisdiction disclosure — ADR 0003, ADR 0004.
- Identity changes surface as visible warnings (TOFU), with explicit accept — ADR 0006, spike-01 demonstrated `UntrustedIdentityError` correctly fires.
- Post-MVP: safety numbers UI for out-of-band key verification (ADR 0006 hardening milestone).

## Layer 1 — Content crypto (E2EE)

Signal protocol profile: X3DH session setup, Double Ratchet per-message keys, AES-256-GCM bodies, PQXDH post-quantum enabled by default in the SDK — ADR 0006, validated on real Android hardware in spike-01.

Properties: forward secrecy, post-compromise self-healing, and the relay only ever sees opaque envelopes.

## Layer 2 — Local storage (vault)

Message history, contacts, and cached translations live in IndexedDB encrypted at rest under a key derived from the user's passphrase — ADR 0005. Stealing the device storage yields ciphertext. The vault passphrase is local-only and never entangled with the account password (ADR 0007).

## Layer 3 — Device authentication (no stealable token)

Sensitive relay operations (mailbox fetch, prekey publish, envelope send) are gated by a per-connection challenge: the server issues a single-use random nonce on WebSocket connect; the app signs it with the device Signal identity key — ADR 0007.

The prekey directory itself is contact-gated (ADR 0006, revised): bundles are served only to accepted contacts, and one-directionally to the target of a pending contact request. This prevents address/device enumeration and blocks one-time-prekey pool draining by strangers.

Nothing with mailbox power persists in browser storage. Reconnecting requires a fresh signature against a fresh nonce; there is no long-lived device token to steal. A password compromise does not open the mailbox — the attacker would need to register a new device, which contacts observe as an identity change (Layer 0 closes the loop).

## Layer 4 — Account authentication (bounded blast radius)

Social identity uses self-hosted username + password with Argon2id and rate limiting — ADR 0007. Sessions are short-lived JWTs (~15 min) plus refresh tokens with rotation and reuse detection; the worst case of theft is minutes of social-scoped endpoints, never the mailbox or prekeys. No external IdP: login metadata stays inside our own service, consistent with the privacy baseline.

## Layer 5 — Browser surface (XSS is the root threat)

Every browser-side control above collapses if script from elsewhere runs in the PWA origin: injected JS could sign challenges and read decrypted plaintext. Therefore — RNF-05:

- Strict Content-Security-Policy.
- No third-party scripts in the PWA (this is also why OAuth IdPs were rejected in ADR 0007).
- Dependencies pinned; builds reproducible.

## Layer 6 — Server posture (dumb relay)

The backend stores envelopes' ciphertext with TTL, never plaintext; the translation proxy forwards consented text in memory and logs nothing — ADR 0001, ADR 0002, ADR 0003. Database compromise yields opaque envelopes and hashed credentials.

## Threat summary

| Threat | Layer that answers it |
| --- | --- |
| Curious/compromised server | 1 (E2EE), 6 (dumb relay) |
| Stolen device / dumped IndexedDB | 2 (vault at rest) |
| Stolen account credentials | 3 (device signature), 4 (short-lived JWT) |
| Prekey-bundle injection / MITM | 3 (signed registration), 0 (identity-change UX) |
| Address enumeration / OTP-pool drain | 3 (contact-gated key directory) |
| Phishing / consent fatigue | 0 (explicit per-use consent, visible warnings) |
| XSS in the PWA origin | 5 (CSP, no third-party JS) |
| Traffic analysis / login metadata | 4 (no external IdP), 6 (minimal retention) |

## Explicitly out of scope for MVP

Sealed sender (sender metadata hiding from the relay), multi-device key coordination, group protocols — deferred per ADR 0006; each has a documented escalation path.
