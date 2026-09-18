# Decisions log

This file tracks significant design decisions for Let's Chat and their status. Detailed rationale lives in the ADR documents; update this log whenever an ADR is added, accepted, revised, or superseded.

## Product decisions

| Decision | Status | Date | Notes |
| --- | --- | --- | --- |
| Frontend: Vite + React + TypeScript, delivered as a PWA | Accepted | 2026-09-16 | Set during product definition |
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
| 0001 | [Backend architecture and language](./adr/0001-backend-architecture-and-language.md) | Accepted | 2026-09-16 |
| 0002 | [Persistence strategy](./adr/0002-persistence-strategy.md) | Accepted | 2026-09-16 |
| 0003 | [Translation engine](./adr/0003-translation-engine.md) | Accepted | 2026-09-16 |
| 0004 | [Translation provider catalog](./adr/0004-translation-provider-catalog.md) | Accepted | 2026-09-16 |
| 0005 | [Message storage and backup](./adr/0005-message-storage-and-backup.md) | Accepted | 2026-09-16 |
| 0006 | [E2EE protocol and key management](./adr/0006-e2ee-protocol.md) | Accepted | 2026-09-16 |

## Pending decisions (future ADRs)

- **Auth model** — credential design that does not weaken E2EE (passphrase-derived vault keys vs account recovery); how device registration authenticates.
- **Group chat** — triggers the deferred MLS evaluation from ADR 0006.
- **Multi-device** — requires Sesame-style device coordination (ADR 0006) plus the backup/seed flow (ADR 0005).
