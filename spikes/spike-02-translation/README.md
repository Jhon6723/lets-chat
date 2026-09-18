# Spike 02 — Translation provider benchmark

**Question:** can DeepL and Gemini Flash deliver clean zh→es translations of real informal chat messages fast enough, and does the backend-owned system prompt keep the LLM output clean?

**Context:** de-risks ADR 0003 (translation engine) and ADR 0004 (provider catalog) before building the backend proxy. Disposable code.

## Setup

- Plain Node script (no framework — the spike tests provider quality/latency, not NestJS)
- DeepL free tier (`:fx` key → api-free.deepl.com)
- DeepSeek `deepseek-flash` via OpenAI-compatible endpoint (prepaid balance, ~¥10 top-up)
- Gemini was evaluated and dropped mid-spike — see findings
- `.env` for keys (gitignored — never commit)

## How to run

```bash
cd spikes/spike-02-translation
cp .env.example .env   # fill in keys
npm run bench            # both providers
npm run bench -- deepl   # or just one
npm run bench -- deepseek
BENCH_IDS=plain,slang npm run bench -- deepseek   # subset, for tight quotas
```

Outputs persist to `results.json` so partial runs (quota exhaustion) still leave usable data.

## Message battery

Ten messages stressing real failure modes: plain sentence, colloquialism, emojis, zh/es code-switching, slang (我裂开了), @mention + URL, idioms (看着办), tone/register (阴阳怪气), multi-line, long message.

## Results

### Latency

| Provider | Success | avg | p50 | p95 | Threshold |
| --- | --- | --- | --- | --- | --- |
| DeepL | 10/10 | 545 ms | 518 ms | 1076 ms | < 500 ms target — near miss on avg, acceptable |
| DeepSeek (thinking OFF) | 10/10 | 1000 ms | 1004 ms | 1348 ms | < 1500 ms — **PASS** |
| DeepSeek (thinking ON, default) | 10/10 | 3211 ms | 2538 ms | 7902 ms | — **FAIL**, must disable thinking |
| Gemini 3.6 Flash | 4/10 (quota) | 6662 ms | 7505 ms | 10165 ms | < 1500 ms — **FAIL**, dropped |

### Output quality (zh → es)

| Input | DeepL | DeepSeek | Notes |
| --- | --- | --- | --- |
| 你吃饭了吗？ | ¿Has comido ya? | ¿Ya comiste? | both correct; DeepSeek more colloquial (Latin American register) |
| 这也太离谱了吧 | Esto es demasiado descabellado | Esto es demasiado increíble | both fine |
| 我裂开了，老板又改需求了 | Estoy al límite, el jefe ha vuelto a cambiar los requisitos | Me estoy volviendo loco, el jefe volvió a cambiar los requisitos | slang rendered well by both |
| 我mañana要开会，你别迟到 | Mañana tengo una reunión, no llegues tarde. | Mañana tengo una reunión, no llegues tarde. | code-switch handled by both |
| @小明 快看这个 https://example.com 笑死我了哈哈哈 | **@Xiaoming** ¡mira esto! | **@小明** ¡Mira esto! | **DeepL transliterates the @mention — breaks real mention tokens; DeepSeek preserves it exactly per the system prompt** |
| 这事儿你看着办吧 | Esto lo decides tú | Haz lo que quieras con esto | both fine |
| 你别阴阳怪气的 | tono enigmático | No seas sarcástico | DeepSeek's rendering is more natural |
| multi-line | preserved | preserved | both correct |

## Findings

- **DeepL is production-viable today.** Sub-second latency, clean output, preserves formatting/URLs, handles code-switching and slang well. One flaw measured: it transliterates @mentions (小明 → @Xiaoming), which breaks real mention tokens — the proxy should protect `@token` spans before sending, or prefer the LLM provider for messages containing mentions.
- **DeepSeek passes and beats DeepL on the differentiating pair.** With thinking mode disabled (`{"thinking": {"type": "disabled"}}`) it hits ~1s avg / 1.35s p95 — inside the 1.5s threshold for an explicit "Translate" action. Output is more colloquial/natural, preserves @mentions exactly, and it is OpenAI-compatible so the generic adapter covers it. No free tier, but prepaid balance costs ~$0.0001-0.0002 per message — ¥10 ≈ thousands of translations. Rate limits are concurrency-based (2500 for Flash), not daily — nothing like the Gemini cliff.
- **DeepSeek thinking mode is ON by default and must be explicitly disabled** for translation — with it on, latency triples (~3.2s avg, 7.9s p95) and fails the threshold.
- **Gemini Flash free tier is not viable for the MVP.** Hard limits of ~20 requests/day and 5/minute made most of the battery fail with 429s; the model also intermittently returned 503s under load. Latency when it did respond was 3.7–10s. Removed from the catalog (ADR 0004 revised 2026-09-17).
- **Model availability churned mid-spike:** `gemini-2.5-flash` and `gemini-2.0-flash` return 404 for new keys — only `gemini-3.6-flash` was offered. Provider adapters must treat the model name as config, not a constant.
- **Retry/backoff is mandatory** for the LLM path (503/429 are routine), not an edge case — the harness already implements it and the real TranslationProvider port should too.

## Open questions for ADR 0004

1. ~~Gemini viability~~ — resolved: excluded, spike-02 evidence.
2. Should the proxy protect `@mention` spans and URLs before sending to providers? DeepL demonstrably mangles @mentions — either pre-process or prefer DeepSeek when mentions are present.
3. Is ~500ms avg acceptable for DeepL / ~1s for DeepSeek, or do we need to measure perceived latency end-to-end (PWA → VPS → provider → back)?
4. DeepSeek jurisdiction note (Chinese provider) — confirmed it stays in the consent sheet per ADR 0004.

## Notes for the real implementation

- Keys live server-side only (backend proxy) — the spike used `.env` for convenience; the real path is backend config + optional BYOK from the vault.
- No request-body logging anywhere — the harness prints translations to stdout because it is a local diagnostic; the real proxy must not persist or log plaintext.
- Provider adapters behind a `TranslationProvider` port (hexagonal), with the system prompt owned by the backend.
