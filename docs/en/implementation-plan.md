# Implementation plan

Ordered build plan for the MVP, organized as vertical slices — each phase ends with something working end-to-end. References the ADRs each task implements.

## Phase 0 — Contract safety net

Closes the loop on the ADR-0001 revised trade-off before any wire code grows.

| # | Task | Ref |
| --- | --- | --- |
| 0.1 | Fixture generator in `shared/protocol` — script emitting canonical JSON samples of EncryptedEnvelope, PreKeyBundle, and all RelayClientMessage/RelayServerMessage variants | ADR 0001 |
| 0.2 | `backend/tests/LetsChat.ContractTests/` — xUnit suite deserializing each fixture into the C# records and asserting field-by-field + round-trip | ADR 0001 |
| 0.3 | Regeneration script wired as a repo-level command (npm script or Makefile) so fixtures are refreshed on protocol changes | ADR 0001 |

Done when: deliberately renaming a TS field without touching the C# record makes the contract test fail.

## Phase 1 — Relay vertical slice (backend)

The dumb relay working end-to-end over real persistence.

| # | Task | Ref |
| --- | --- | --- |
| 1.1 | EF Core + Postgres: DbContext, EnvelopeStore adapter replacing the in-memory one, first migration; docker-compose postgres already exists | ADR 0001, ADR 0002 |
| 1.2 | Account registration + login: username + Argon2id password hash; login rate limiting | ADR 0007 |
| 1.3 | Account session issue: short-lived JWT (~15 min, account scope) + refresh token with rotation and reuse detection (stale refresh → invalidate token family) | ADR 0007 |
| 1.4 | Device binding: register a device under the account; the registration request must be signed by the device's Signal identity key | ADR 0006, ADR 0007 |
| 1.5 | Device-signature auth — no stealable token: WS connect → server issues single-use nonce (short TTL) → app signs with device identity key → connection authenticated; sensitive REST ops (prekey publish, mailbox fetch) verified the same way | ADR 0006, ADR 0007 |
| 1.6 | Prekey bundle REST endpoints (publish own bundle, fetch contact's bundle) behind device-signature auth; fetch gated to contact edges — executes after 1.7 | ADR 0006, ADR 0007 |
| 1.7 | Contacts REST endpoints (find user by username, request/accept/decline/block, list/remove) behind account JWT — runs before 1.6 because prekey fetch depends on contact edges | ADR 0001, ADR 0007 |
| 1.8 | WS relay flow: send → enqueue/push, fetch_pending, ack → markDelivered; recipient identity derived from the authenticated connection, not client-supplied fields | ADR 0001 |
| 1.9 | Re-validate the spike-01 subscription quirk (push path not firing onMessageDecrypted) against the real relay | spike-01 |

Done when: two accounts register, log in (JWT + refresh rotation), bind devices by signature, and exchange envelopes through the relay authenticated via per-connection nonce signatures — with Postgres persistence and ack semantics.

## Phase 2 — Frontend crypto + vault

Port the validated spike-01 machinery into the real app.

| # | Task | Ref |
| --- | --- | --- |
| 2.1 | CryptoEngine port + SDK adapter in `frontend/` (carry over spike-01 learnings: markDelivered, acceptIdentityRotation, IndexedDB store quirks) | ADR 0006, spike-01 |
| 2.2 | Vault: IndexedDB encrypted at rest with passphrase-derived key; message history + cached translations live here | ADR 0005 |
| 2.3 | WS client adapter: connect → sign server nonce with device identity key (per-connection auth) → send envelope, fetch pending, markDelivered, reconnect with backoff (re-signing the fresh nonce on each reconnect) | ADR 0001, ADR 0007 |
| 2.4 | Auth client: register/login calls, JWT kept in memory (never localStorage), refresh with rotation handling, session expiry UX | ADR 0007 |

Done when: the app registers/logs in, creates an identity, unlocks a vault, and round-trips an encrypted envelope to the relay over a signature-authenticated connection.

## Phase 3 — Chat loop (MVP core)

The actual product surface: 1:1 messaging.

| # | Task | Ref |
| --- | --- | --- |
| 3.1 | Onboarding flow: account registration/login UI, identity key generation, device binding, passphrase/vault setup, identity fingerprint display — account password and vault passphrase visibly distinct per ADR 0007 | ADR 0006, ADR 0007 |
| 3.2 | Contacts + chat list (desktop-first layout per wireframe) | requirements RF |
| 3.3 | Chat view: send/receive, decrypted rendering, persisted history from vault | requirements RF |
| 3.4 | TOFU identity-change confirmation UI on UntrustedIdentityError (acceptIdentityRotation behind user consent — not silent) | ADR 0006, spike-01 |
| 3.5 | Offline/reconnect UX: pending fetch on reconnect, offline banner | requirements RF |

Done when: two browsers chat 1:1 end-to-end encrypted with history surviving reloads.

## Phase 4 — Translation

The differentiating feature, consent-gated.

| # | Task | Ref |
| --- | --- | --- |
| 4.1 | Backend translation proxy endpoint: DeepL managed default + DeepSeek BYOK adapter (thinking disabled always), in-memory forward only | ADR 0003, ADR 0004 |
| 4.2 | BYOK key storage + verify endpoint | ADR 0004 |
| 4.3 | Consent flow: global toggle (default OFF) + per-use sheet with provider + jurisdiction disclosure | ADR 0003 |
| 4.4 | Per-message translate action: decrypted text → proxy → rendered with provider attribution; result cached in vault | ADR 0003, ADR 0005 |
| 4.5 | @mention/URL span protection before provider calls (spike-02 finding: DeepL transliterates 小明 → @Xiaoming) | spike-02 |

Done when: a user translates a zh message to es with explicit consent and the result caches in the vault.

## Phase 5 — PWA hardening

| # | Task | Ref |
| --- | --- | --- |
| 5.1 | Install prompt, manifest polish, offline shell behavior | RF-30 |
| 5.2 | Strict CSP, dependency pinning review, minimal third-party scripts — XSS is the E2EE killer | RNF-05, ADR 0006 |
| 5.3 | Android PWA smoke pass (install, reconnect, crypto perf re-check) | spike-01 |

## Explicitly out of scope for MVP

Safety numbers UI, multi-device, group chats/MLS, encrypted cloud backup, additional providers, self-hosted translation module — all tracked in the post-MVP backlog (requirements.md) and pending-decisions list (decisions.md).

## Blocking open decisions

None — the auth model was resolved in ADR 0007 (self-hosted credentials + device-signature for sensitive ops). Remaining pending decisions (group chat, multi-device) are post-MVP scope.
