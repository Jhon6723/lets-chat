# ADR 0004: Translation provider catalog exposed to users

- Status: Accepted (revised 2026-09-17 after spike-02: Gemini removed, DeepSeek added)
- Date: 2026-09-16

## Context

ADR 0003 established that translation is performed client-side after decryption through a provider of the user's choice, proxied by our backend, with two-layer consent. Users may also supply their own API key (BYOK). This ADR defines **which providers appear in the settings catalog** — a curation decision, not just a technical one.

Curation matters because each catalog entry carries three user-facing costs:

1. **Quality for the pairs we care about** — zh-es is the differentiating pair; a provider weak on it is a trap for users who don't know better.
2. **Key-acquisition UX** — BYOK only works if a non-technical user can actually get a key. DeepL takes ~3 minutes in a web form; Azure requires an Azure account, a subscription, a resource, and a region — a hostile flow for consumers.
3. **Privacy surface** — each provider is a different third party reading the user's plaintext. The catalog is also a disclosure menu; a provider's jurisdiction and data-retention terms belong in the consent UI.

## Candidate evaluation

### NMT providers

| Provider | zh-es quality | Price | Free tier | Key-acquisition UX | Verdict |
| --- | --- | --- | --- | --- | --- |
| DeepL | Good — leader in European output, decent zh | ~$25/M chars | 500K chars/mo | Easy — signup gives a key | Include |
| Google Cloud Translation | Fair-good, stiff on idioms | $20/M chars | 500K/mo | Poor — GCP console, billing account, service account | Include (recognizable brand) |
| Azure Translator | Mid-tier | $10/M chars | 2M chars/mo | Poor — full Azure resource provisioning | Exclude |
| Amazon Translate | Mid-tier | $15/M chars | 2M/mo, 12 months only | Hostile — IAM credentials | Exclude |

Azure and Amazon are excluded not on quality but on **key-acquisition hostility**: asking a chat user to provision an Azure resource is a dead feature. Both remain reachable via the generic adapter (below) for power users.

### LLM providers

LLMs lead quality on Chinese pairs on paper (independent 2026 COMET benchmarks: frontier LLMs ~0.87-0.90 vs DeepL-class NMT lower on zh output), but spike-02 measured the operational reality.

| Provider | zh-es quality | Price | Free tier | Key-acquisition UX | Verdict |
| --- | --- | --- | --- | --- | --- |
| Gemini (Flash) | High on benchmarks | ~$1/M words | 20 req/day, 5 req/min | Easy — AI Studio gives a key in one click | **Excluded — measured in spike-02, see below** |
| DeepSeek | **Best for Chinese pairs** — 0.901 COMET ZH-EN; spike-02 confirmed | ~$0.0002/msg measured | Prepaid balance, no free tier | Medium — Chinese platform, English docs | **Include — measured in spike-02, see below** |
| OpenAI / Claude direct | Highest overall | $20+/M words | No | Medium | Covered by generic adapter |
| OpenAI-compatible endpoint | Depends on backend (covers Groq, Mistral, OpenRouter, Ollama, LM Studio) | Varies | Varies | N/A — power users bring URL + key | Post-MVP as "Custom (OpenAI-compatible)" |

**Gemini exclusion — spike-02 evidence (2026-09-17):** the free tier hard-limits at ~20 requests/day and ~5/minute per project-model, which is below any real chat volume and even below comfortable development iteration; observed latency on successful calls was 3.7–10s against a 1.5s threshold; and Google retires model names quickly (`gemini-2.0-flash` and `gemini-2.5-flash` already 404 for new keys, only `gemini-3.6-flash` offered). As a BYOK option it would work, but the product owner decided to remove it from the catalog entirely rather than ship a provider that only functions on a paid tier we cannot validate now.

**DeepSeek inclusion — spike-02 evidence (2026-09-17):** with thinking mode explicitly disabled (`{"thinking": {"type": "disabled"}}` — on by default and it triples latency) it measured ~1s avg / 1.35s p95, inside the 1.5s threshold; 10/10 success; output more colloquial than DeepL and it preserves @mentions exactly where DeepL transliterates them (小明 → @Xiaoming breaks real mention tokens). Quotas are concurrency-based (2500 on Flash), not daily — no quota cliff. Cost is ~$0.0001–0.0002 per chat message on prepaid balance; BYOK via platform.deepseek.com (supports WeChat top-up).

DeepSeek caveat: it is a Chinese provider. For an app whose pitch partly targets users dissatisfied with WeChat, sending plaintext to a Chinese endpoint deserves an explicit notice in the consent sheet — same disclosure rule, plus a jurisdiction note.

### Managed default

For users who configure nothing, our backend holds one managed provider (subject to our quotas and cost controls). Decision: **DeepL** — the only measured provider that meets the latency threshold (~545 ms avg, ~1s p95 in spike-02) with strong zh-es output. Managed-key quotas and per-user cost controls apply.

## Decision

**The MVP catalog ships with two options: DeepL (NMT, managed default + BYOK) and DeepSeek (LLM, BYOK).** DeepSeek was reinstated into the MVP after spike-02 measured it viable — decided by the product owner.

MVP entries:

| Catalog entry | Type | Notes |
| --- | --- | --- |
| **DeepL** | NMT | Managed default key for the app, and BYOK supported (free tier exists for the user's own key) |
| **DeepSeek** | LLM | BYOK only (prepaid balance, no free tier — ¥10 ≈ thousands of translations); consent sheet carries the jurisdiction note; adapter must send `thinking: disabled` |

Post-MVP candidates, in rollout order:

| Catalog entry | Type | Phase | Notes |
| --- | --- | --- | --- |
| **Google Cloud Translation** | NMT | Post-MVP | BYOK; key UX friction documented in UI |
| **Custom (OpenAI-compatible)** | LLM | Post-MVP | BYOK + base URL; covers Groq, Mistral, OpenRouter, Ollama, and our eventual self-hosted module (ADR 0003, Option B') |

Each entry in the UI shows: provider name, engine type, a one-line quality/cost hint, and — on the consent sheet — who processes the plaintext. The TranslationProvider port abstracts all of them; adapters differ only in auth shape and, for LLMs, the injected system prompt (owned by the backend, per ADR 0003).

**Excluded deliberately:** Gemini (spike-02: free-tier quotas below real usage, latency 3.7–10s, rapid model-name churn), Azure and Amazon (hostile key UX; still reachable later via the generic adapter), and any provider whose consumer-facing key flow does not exist.

## Consequences

### Positive

- Minimal MVP surface: two adapters to build and test, covering both engine families (NMT and LLM) — this already exercises the port abstraction and the LLM system-prompt path.
- DeepL as managed default requires zero setup from the user; DeepSeek BYOK gives power users the strongest zh option.
- The generic OpenAI-compatible adapter (post-MVP) future-proofs the catalog: any new LLM provider, including our eventual self-hosted module (Option B' of ADR 0003), plugs in without a new adapter.
- Consent UI can be honest per-provider, including jurisdiction notices for future entries (e.g. DeepSeek).

### Negative and mitigations

- **Two adapters to test.** Mitigation: one shared adapter interface; contract tests against recorded fixtures; post-MVP adapters follow the same contract.
- **No managed LLM option in the MVP.** Users wanting LLM quality must supply a DeepSeek key (prepaid balance). Accepted: a managed LLM option is a post-MVP candidate.
- **Provider churn/deprecation.** Mitigation: catalog is backend-driven config, not hardcoded in the client — entries can be added or removed without a PWA release.

### Revisit triggers

Re-evaluate the catalog if: a provider's zh-es quality regresses; a provider changes terms on message-data usage; the managed-default cost exceeds budget; or a privacy-critical jurisdiction concern emerges for any listed provider.
