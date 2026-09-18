# Requirements

Let's Chat — an end-to-end encrypted chat PWA with built-in, opt-in, high-quality translation.

Scope: **MVP** — 1:1 chats, single device per user, desktop-first web PWA, also installable and usable on Android. Post-MVP items are listed separately and are not binding for the MVP.

Each requirement carries an ID for traceability (RF-xx functional, RNF-xx non-functional) and references the ADR(s) that motivated it.

## Functional requirements (RF)

### Messaging and delivery

| ID | Requirement | Source |
| --- | --- | --- |
| RF-01 | A user can register an account and a device; on registration the client generates an identity keypair and a prekey batch, uploading only public material to the server | ADR 0006 |
| RF-02 | A user can send a text message to a contact; the client encrypts it per-recipient-device before transmission | ADR 0006 |
| RF-03 | The server delivers envelopes in real time over WebSocket to online recipients | ADR 0001 |
| RF-04 | Envelopes for offline recipients are stored server-side (ciphertext only) and delivered when the recipient connects; envelopes are deleted on delivery acknowledgement or TTL expiry | ADR 0002, ADR 0006 |
| RF-05 | The client persists message history in a local encrypted vault; history is available after app restart while the device retains its data | ADR 0005 |
| RF-06 | The client can add and manage contacts (request, accept, decline, block) | Product definition |

### End-to-end encryption

| ID | Requirement | Source |
| --- | --- | --- |
| RF-10 | Session establishment uses X3DH against the recipient's published prekey bundle | ADR 0006 |
| RF-11 | Message keys derive from a Double Ratchet; consumed chain keys are destroyed (forward secrecy, post-compromise security) | ADR 0006 |
| RF-12 | Message encryption is AEAD (AES-256-GCM); tampered envelopes are rejected by the client | ADR 0006 |
| RF-13 | The envelope embeds the sender's declared language inside the ciphertext, not in cleartext metadata | ADR 0003, ADR 0006 |
| RF-14 | The local vault encrypts history at rest under a key derived from the user's passphrase; the vault key is held in memory only while unlocked | ADR 0005 |

### Translation

| ID | Requirement | Source |
| --- | --- | --- |
| RF-20 | A received message in a language different from the user's shows a "Translate" action; language detection runs on the client after decryption | ADR 0003 |
| RF-21 | Translation is disabled by default; enabling it requires the settings toggle (consent layer 1) | ADR 0003 |
| RF-22 | First use of translation in a context shows a consent sheet naming the provider (consent layer 2), with choices: always for this conversation / just this message / cancel | ADR 0003 |
| RF-23 | Translation requests are proxied through the backend; the backend forwards plaintext in memory and neither persists nor logs message bodies | ADR 0003 |
| RF-24 | The MVP provider catalog offers two options: DeepL (managed default + BYOK) and DeepSeek (BYOK, LLM) | ADR 0004 |
| RF-25 | The user can select the translation provider in settings and optionally supply their own API key; user keys are stored client-side in the encrypted vault and sent per request, never persisted by the backend | ADR 0003, ADR 0004, ADR 0005 |
| RF-26 | For LLM providers, the backend injects the translate-only system prompt; the prompt is not user-editable | ADR 0003, ADR 0004 |
| RF-27 | Translations are cached in the local vault; re-viewing a translated message does not re-call the provider | ADR 0005 |

### Platform

| ID | Requirement | Source |
| --- | --- | --- |
| RF-30 | The app is installable as a PWA (manifest, service worker, standalone display) — desktop-first layout, also installable on Android | Product definition |
| RF-31 | The app shell loads offline; message send/receive requires connectivity | Product definition |

## Non-functional requirements (RNF)

### Security and privacy

| ID | Requirement | Source |
| --- | --- | --- |
| RNF-01 | The server never has access to message plaintext at rest or in the messaging path; ciphertext envelopes and public keys only | All ADRs |
| RNF-02 | Private keys are never transmitted and are stored non-extractably where the platform allows | ADR 0006 |
| RNF-03 | All client-server traffic uses TLS (HTTPS/WSS); the database is reachable only on the private Docker network | ADR 0002 |
| RNF-04 | The translation proxy endpoint does not log or persist request/response bodies; it is covered by the threat model and rate-limited | ADR 0003 |
| RNF-05 | The PWA ships a strict CSP, pins dependencies, and minimizes third-party scripts — XSS is the primary E2EE threat in a browser context | ADR 0006 |
| RNF-06 | The disclosed product promise is: E2EE between users, except messages the user explicitly asks to translate | ADR 0003 |

### Performance and capacity

| ID | Requirement | Source |
| --- | --- | --- |
| RNF-10 | Message delivery latency (online recipient) under 1 second at MVP load | ADR 0001 |
| RNF-11 | Translation round-trip acceptable up to ~1.5 s (LLM providers) — translation is an explicit user action, not an ambient feature | ADR 0004 |
| RNF-12 | A single VPS-sized instance handles the MVP user base; the architecture must not block horizontal scaling of the relay later | ADR 0001, ADR 0002 |

### Operability and cost

| ID | Requirement | Source |
| --- | --- | --- |
| RNF-20 | Total infrastructure cost stays in single-digit EUR/month at MVP scale (one VPS hosting backend + Postgres in Docker) | ADR 0002 |
| RNF-21 | Nightly pg_dump backups to offsite object storage with retention; a documented restore drill runs quarterly | ADR 0002 |
| RNF-22 | Envelope TTL pruning is enforced and covered by tests — it is a privacy guarantee, not housekeeping | ADR 0002 |
| RNF-23 | Managed-translation usage is metered per user with quotas to bound provider costs | ADR 0003 |

### Usability

| ID | Requirement | Source |
| --- | --- | --- |
| RNF-30 | No model downloads or heavy assets are required at install; the PWA stays lightweight on mid-range Android | ADR 0003 |
| RNF-31 | Consent disclosures are shown at the moment of use, in plain language, never as blanket terms-and-conditions | ADR 0003 |
| RNF-32 | Settings clearly states that history lives only on this device | ADR 0005 |

### Maintainability

| ID | Requirement | Source |
| --- | --- | --- |
| RNF-40 | Backend follows hexagonal architecture: domain and application layers import nothing from NestJS; framework code lives in adapters only — enforced by dependency-boundary linting | ADR 0001 |
| RNF-41 | Crypto engine and translation providers are behind ports (CryptoEngine, TranslationProvider), replaceable without domain changes | ADR 0001, ADR 0003, ADR 0006 |
| RNF-42 | Shared TypeScript protocol types are defined once in a monorepo package consumed by client and server | ADR 0001 |

## Post-MVP backlog (not binding)

- Encrypted cloud backup/restore of the vault (ADR 0005, Option B)
- Multi-device support (Sesame-style coordination, per-device sessions)
- Group chats (triggers the deferred MLS evaluation, ADR 0006)
- Post-quantum key agreement (PQXDH) enablement (ADR 0006)
- Safety numbers / identity verification UI (ADR 0006 hardening milestone)
- Additional translation providers: Google Cloud Translation, OpenAI-compatible custom endpoint (ADR 0004 rollout order)
- Self-hosted translation module replacing external providers (ADR 0003, Option B')
- Password/argon-grade KDF upgrade if browser-native Argon2id becomes viable (ADR 0005)
