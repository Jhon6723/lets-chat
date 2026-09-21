# Decisions log

This file tracks significant design decisions for Let's Chat and their status. Detailed rationale lives in the ADR documents; update this log whenever an ADR is added, accepted, revised, or superseded.

## Product decisions

| Decision | Status | Date | Notes |
| --- | --- | --- | --- |
| Frontend: Vite + React + TypeScript, delivered as a PWA | Accepted | 2026-09-16 | Set during product definition |
| Backend: ASP.NET Core (.NET), hexagonal architecture | Accepted | 2026-09-18 | Supersedes the original NestJS choice before real backend code existed. Owner decision: developer experience (solo dev's production stack is .NET) + cleaner ports-and-adapters fit via built-in DI vs NestJS's modular-monolith idiom |
| Wire-protocol types duplicated across TypeScript (client) and C# (server), kept honest by contract validation tests | Accepted | 2026-09-18 | Consequence of the cross-language backend — shared TS package benefit traded away; schema codegen evaluated and deferred (small stable protocol, contract tests suffice; codegen is the documented escalation path) |
| Realtime transport: raw WebSocket middleware (not SignalR) | Accepted | 2026-09-18 | Relay contract is minimal (deliver/ack/fetch-pending); E2EE reconnect needs app-level resync anyway — SignalR only restores transport and adds hub framing + client lib coupling |
| Auth model: self-hosted username/password (Argon2id) for social identity + device-signature challenge for sensitive relay ops | Accepted | 2026-09-19 | Owner decision framed by the product pitch — privacy as baseline, external services only opt-in/BYOK; external IdPs rejected (login metadata to third parties, PWA XSS surface) and deferred to optional post-MVP social linking |
| Platform priority: desktop web first, Android PWA second | Accepted | 2026-09-17 | Owner decision: design and layout target desktop browsers first; Android PWA remains supported — responsive PWA covers both, spike-01 already validated crypto on Android |
| Translation happens client-side after message decryption | Accepted (design constraint) | 2026-09-16 | Server-side translation of ciphertext is impossible under E2EE; engine selection resolved in ADR 0003 |
| Translation consent model: two-layer (global toggle default OFF + per-use sheet), never blanket terms-and-conditions | Accepted | 2026-09-16 | Settled in ADR 0003 discussion; product promise becomes "E2EE between users, except messages the user explicitly asks to translate" |
| Translation engine: external provider (DeepL) proxied through backend, client-side after decryption — no on-device models | Accepted | 2026-09-16 | Owner decision: avoid ~470 MB model downloads and RAM pressure on mid-range Android; self-hosted module is the planned evolution |
| Translation provider is user-configurable in settings, including bring-your-own-API-key (BYOK) | Accepted | 2026-09-16 | MVP catalog: DeepL (managed default + BYOK) and DeepSeek (BYOK); post-MVP adds Google Cloud Translation and an OpenAI-compatible custom endpoint |
| System prompts for LLM translation providers are owned by the backend adapter, not user-editable in v1 | Accepted | 2026-09-16 | Applies to post-MVP LLM providers; NMT providers (DeepL/Google/Azure) have no prompt surface |
| Gemini removed from provider catalog | Accepted | 2026-09-17 | Spike-02 measured: free tier ~20 req/day and ~5 req/min (below real chat volume), 3.7–10s latency vs 1.5s threshold, rapid model-name churn |
| DeepSeek added to MVP catalog as BYOK LLM option | Accepted | 2026-09-17 | Spike-02 measured: ~1s avg / 1.35s p95 with thinking disabled, preserves @mentions (DeepL transliterates them), concurrency-based quotas, ~$0.0002/message. Consent sheet carries Chinese-jurisdiction note |
| Project is open source (MVP) | Accepted | 2026-09-16 | Makes the AGPL-3.0 license of signal-protocol-sdk compatible; if distribution goes closed/commercial later, the CryptoEngine port allows swapping the crypto library |

## Architecture decision records

| ADR | Title | Status | Date |
| --- | --- | --- | --- |
| 0001 | [Backend architecture and language](./adr/0001-backend-architecture-and-language.md) | Accepted (revised 2026-09-18) | 2026-09-16 |
| 0002 | [Persistence strategy](./adr/0002-persistence-strategy.md) | Accepted | 2026-09-16 |
| 0003 | [Translation engine](./adr/0003-translation-engine.md) | Accepted | 2026-09-16 |
| 0004 | [Translation provider catalog](./adr/0004-translation-provider-catalog.md) | Accepted | 2026-09-16 |
| 0005 | [Message storage and backup](./adr/0005-message-storage-and-backup.md) | Accepted | 2026-09-16 |
| 0006 | [E2EE protocol and key management](./adr/0006-e2ee-protocol.md) | Accepted | 2026-09-16 |
| 0007 | [Authentication and identity providers](./adr/0007-authentication-and-identity-providers.md) | Accepted | 2026-09-19 |

## Pending decisions (future ADRs)

- **Group chat** — triggers the deferred MLS evaluation from ADR 0006.
- **Multi-device** — requires Sesame-style device coordination (ADR 0006) plus the backup/seed flow (ADR 0005).
