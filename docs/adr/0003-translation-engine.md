# ADR 0003: Translation engine — on-device model vs external API vs hybrid

- Status: Accepted
- Date: 2026-09-16

## Context

The product's differentiator is a **built-in translator measurably better than WeChat for Chinese-Spanish**. The product's security promise is **end-to-end encryption**: the server only ever holds ciphertext, so translation is physically impossible on the server. Both facts were established in the product definition; this ADR resolves where translation runs and who sees the plaintext.

One structural fact constrains everything else: **translation can only happen after decryption, on a client device**. Whatever engine we pick, plaintext must exist on that device in readable form — the question is whether it then leaves the device at all.

Evaluation drivers, in priority order:

1. **Translation quality for zh-es** — the differentiator. WeChat's translator is the bar to beat, and independent benchmarks consistently rank current engines: LLMs (GPT-class, Claude) and DeepL lead for Chinese↔Spanish; Google Cloud Translation is broad but stiff; on-device NMT models are usable but clearly below the cloud leaders.
2. **Privacy posture** — whether plaintext is disclosed to any third party after decryption.
3. **Cost** — per-message API fees vs. zero marginal cost for a local model.
4. **Feasibility on the target platform** — a PWA on mid-range Android, with limited RAM, metered data, and no native-runtime access.
5. **Latency** — chat translation should feel near-instant.

## Options considered

### Option A: On-device NMT model in the browser (Transformers.js)

Run a quantized multilingual model — practically, **Meta NLLB-200 distilled 600M** (ONNX, q4/q8 via onnxruntime-web / Transformers.js, WASM or WebGPU) — inside a Web Worker in the PWA.

**Pros:**

- **Perfect privacy**: plaintext never leaves the device; no third party is added to the trust model. Fully consistent with E2EE.
- **Zero marginal cost** per translation; no API key management, no billing surprises, works offline after download.
- Predictable behavior; no provider rate limits or ToS changes.

**Cons:**

- **Weight**: ~470 MB (q4) to ~900 MB (q8) download, cached via Cache API/IndexedDB. Heavy but feasible on Wi-Fi; hostile on metered data. RAM footprint on mid-range Android is the harder constraint — expect the model to be unloadable on low-end devices.
- **Quality ceiling**: NLLB-600M is competent for zh-es (far better than Opus-MT or WeChat's in-app output on low-resource pairs), but it is still a distilled 2022-era NMT model — noticeably below DeepL/LLM output on idioms, register, and long sentences. It beats WeChat; it does not beat the cloud state of the art.
- **Cold-start latency**: first load takes seconds; subsequent translations are fast (sub-second per message on recent hardware).
- Browser built-in translation (Chrome Translator API) was evaluated and **rejected as a fallback**: it is desktop-only and does not run on Android — our primary platform.

### Option B: External translation API called from the client

After decryption, the PWA calls DeepL, Google Cloud Translation, or an LLM endpoint directly, with the user's own awareness and consent.

**Pros:**

- **Best available quality** for zh-es, the actual product differentiator. LLM endpoints additionally handle slang, tone, and mixed-language messages better than NMT APIs.
- **No download weight**: a thin HTTPS call; works on any device that can run the app.
- Fast time-to-market for the MVP — a weekend of integration vs. a model-loading infrastructure.
- Predictable cost: Google ~$20/million chars; DeepL has a 500k chars/month free API tier; LLMs priced per token. A chat workload is small — thousands of short messages.

**Cons:**

- **Privacy tradeoff**: plaintext is disclosed to the translation provider. The E2EE promise narrows from "nobody can read this" to "nobody can read this except the translator you opted into". The server still never sees plaintext — the disclosure is client→provider only — but it is a real, must-be-disclosed weakening of the security model.
- **Ongoing cost** scales with usage; abuse control (rate limiting, per-user quotas) becomes our problem since calls originate from the client.
- Key management: API keys cannot safely live in the client for a paid provider — calls must be proxied through our backend (which still never stores the plaintext; it forwards it in-memory), or users bring their own key. Proxying adds a plaintext-touching endpoint to our infrastructure — a new, narrow attack surface that must be designed carefully (no logging of message bodies, memory-only handling).

### Option B': Self-hosted translation module (no third party)

A variant of Option B raised during review: instead of calling an external provider, the backend hosts its own translation model (e.g. NLLB-1.3B or a small open LLM) in a dedicated container. The client sends plaintext to our own endpoint; no external company ever sees it.

**Pros:**

- **One fewer trust party**: the disclosure chain is user → us, not user → us → DeepL. The user already trusts Let's Chat with their keys and relay; trusting our translator is coherent with that choice.
- **Honest marketing**: "your message only leaves your phone toward us, and only when you ask for it."
- Fits the hexagonal backend cleanly: a TranslationService container behind a TranslationProvider port, same interface as the DeepL adapter.
- Bigger models than a phone can run (more RAM/CPU); quality scales with the VPS.

**Cons:**

- **Does not preserve E2EE** — same fundamental limit as Option B. "We don't store plaintext" is a policy promise, not a cryptographic one: our process sees the message in memory. If the server is compromised or legally compelled, plaintext is exposed. The product framing stays "E2EE by default, explicit opt-in for server-assisted translation" — never "pure E2EE" for translated messages.
- **Quality floor vs cost**: on CPU, NLLB-600M quality is comparable to the on-device model (not better); beating cloud quality needs NLLB-1.3B (~4-6 GB RAM) or an open LLM (GPU, ~$50+/month).
- More infrastructure to operate: model lifecycle, inference scaling, memory pressure on the same host.

### Option C: Hybrid — on-device default, opt-in API translation

Default path is Option A (private, free); an explicit per-message or per-conversation action ("Translate with cloud engine — message text will be sent to provider X") invokes Option B for quality.

**Pros:**

- **User-controlled privacy tiers**: privacy-paranoid users never leak plaintext; convenience-first users get the best quality. This mirrors Signal's approach to optional features that trade metadata for convenience.
- Differentiator preserved: the "good translation" exists (Option B quality), while the privacy story stays intact by default.
- Covers offline scenarios (on-device path keeps working without connectivity).

**Cons:**

- **Double implementation cost**: both engines must be built, maintained, and UX'd.
- Consent UX is delicate: a confusing toggle undermines the E2EE promise. Must be explicit, per-action, never default-on.
- The ~470 MB model remains a hard sell on metered/low-end devices even as an optional feature.

### Option D: Sender-side translation inside the envelope

The sender's client translates the message **before encryption** and embeds both versions in the ciphertext envelope (or encrypts the translation separately per recipient language). The recipient decrypts and already has the translation — no post-decryption network call at all.

**Pros:**

- Zero post-decryption plaintext disclosure on the recipient side; recipient gets instant translation with no model download.
- Translation quality can use a cloud API — but the *sender's* client calls it, so the disclosure decision belongs to the message author, not the reader.
- Works offline on the receiving side.

**Cons:**

- **Wrong consent owner**: the sender may happily send plaintext to an API, but the *recipient* may not want the sender deciding that — and the recipient's privacy isn't the one being spent anyway; it's the message content both share. In group chats this gets philosophically messy.
- Doubles ciphertext size per language; recipient-language preferences must be known to the sender (protocol complexity: per-recipient language hints in cleartext metadata, or translating into every plausible target).
- Only works for languages the sender predicted; a recipient reading in an unanticipated language still needs a fallback path — so Options A or B must exist anyway.
- Adds pre-encryption latency to every send.

## Decision

**Adopt Option B: client-side translation after decryption via an external provider proxied through our backend. The on-device tier (Option A) is rejected — the owner has decided the app will not carry the weight of on-device models (~470 MB download, multi-GB RAM pressure on mid-range Android). Option B' (self-hosted module) remains the planned evolution once the VPS can sustain a bigger model.**

MVP implementation:

- Translation runs **client-side after decryption**, calling a cloud provider **proxied through our backend** (backend forwards in-memory, logs nothing, stores nothing — the relay stays dumb; it just carries one extra ciphertext-free hop it never persists). Direct client-to-provider calls are not possible anyway: a paid API key cannot be embedded in a PWA bundle without being extractable.
- Start with **DeepL API** (best zh/es NMT quality, free tier for development); keep the adapter behind a TranslationProvider port so an LLM endpoint or the self-hosted module (Option B') can be swapped in without client changes.
- **User-configurable provider (BYOK).** In settings, the user selects their translation provider (DeepL in the MVP; Google Cloud Translation, DeepSeek, or an OpenAI-compatible endpoint post-MVP) and may supply their own API key. A user-provided key shifts both the cost and the trust decision to the user — they pick which actor sees their plaintext. Users who configure nothing use our managed default provider (subject to our quotas).
  - Keys are stored client-side (IndexedDB, protected by the same vault encryption as local message data) and transmitted with each translation request; the backend proxy uses the user's key for that call and never persists it.
  - The proxy still mediates all calls for CORS compatibility — several providers do not accept direct browser requests.
- **LLM providers require a system prompt; NMT providers do not.** When the selected provider is an LLM (OpenAI-compatible, DeepSeek), the backend adapter injects a strict system prompt owned by us: translate-only output, no commentary, preserve formatting/emoji/placeholders, detect-and-keep code-switched segments. NMT providers (DeepL, Google, Azure) expose no prompt surface. The system prompt lives in the backend adapter per provider — never user-editable in v1 — so output format stays predictable regardless of which provider is chosen.
- **Consent model: two layers, never blanket terms-and-conditions.**
  - Layer 1 — onboarding/settings: a global toggle, default OFF, labeled along the lines of "Translation uses an external provider (DeepL). Translated messages are sent to them for processing."
  - Layer 2 — moment of use: the first time a user taps "Translate" in a given context, a short sheet states "This message will be sent to DeepL for translation" with choices: always for this conversation / just this message / cancel.
  - **Never silently translate.** The disclosed promise is: "E2EE between users, except for messages the user explicitly asks to translate."

Planned evolution — self-hosted opt-in backend (Option B'):

- When the VPS can sustain it (RAM ~4-6 GB for NLLB-1.3B, or budget for GPU inference), replace the DeepL adapter with our own translation container behind the same TranslationProvider port. The disclosure then simplifies to "sent to Let's Chat translation service" — one trust party instead of two.
- Until then, the privacy policy names DeepL as the upstream processor.

Rationale over alternatives: Option A (on-device) was rejected by the product owner on footprint grounds — a ~470 MB download plus multi-GB WASM memory pressure contradicts a lightweight PWA on mid-range Android, and its quality ceiling (distilled NMT) does not clearly beat the incumbent it aims to displace. Option B' alone cannot deliver the differentiating quality at MVP budget. Option D assigns the privacy decision to the wrong party and still requires B as fallback.

### Protocol implications (binding on the E2EE ADR)

- The envelope format must include an **in-ciphertext language hint** (sender's declared language) — placing it in cleartext metadata would leak message attributes.
- Language **detection runs on the recipient's device** after decryption (small detectors like franc/cld3 work in WASM), not on the server.
- The translation proxy endpoint must be **provably log-free** for request bodies and covered by the threat model.

## Consequences

### Positive

- The product ships its differentiator at cloud-grade quality in the MVP without compromising the server-side E2EE boundary — the server still never sees plaintext persistently.
- Single translation engine to build and maintain; the client stays thin.
- The provider is swappable behind a port: DeepL today, LLM or self-hosted model tomorrow.

### Negative and mitigations

- **Privacy exception exists.** Plaintext reaches a translation provider when the user opts in. Mitigation: two-layer consent UI, off by default, per-message and per-conversation granularity, documented in the threat model and privacy policy.
- **No privacy-preserving translation path exists.** Users who reject any third-party disclosure simply have no translation feature — they read the original. Accepted by the product owner; the self-hosted evolution (Option B') reduces but does not eliminate this gap, since our server still sees plaintext transiently.
- **Proxy endpoint touches plaintext in memory.** Mitigation: no persistence, no logging of bodies, TLS, rate limiting, separate audit. Threat-model it in the E2EE ADR.
- **Cost.** API usage must be metered per user with quotas; abuse case considered in backend design.
- **No offline translation.** Accepted: a PWA chat already requires connectivity for messaging.

### Revisit triggers

Re-evaluate if: the provider cost grows beyond the VPS budget; regulatory/privacy requirements demand zero third-party disclosure by default; a usable sub-200 MB model with competitive zh-es quality emerges (NLLB successors, distilled LLM translators) making a lightweight private tier viable; or the VPS budget allows a GPU/1.3B+ self-hosted model that makes Option B' worthwhile.
