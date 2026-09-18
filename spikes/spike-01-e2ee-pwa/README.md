# Spike 01 — E2EE in a PWA via @open-e2ee/signal-protocol-sdk

**Question:** does the Signal Protocol SDK run correctly and fast enough in a browser PWA, with IndexedDB persistence surviving page reloads, on a mid-range Android device?

**Context:** this spike de-risks ADR 0006 before the real codebase exists. The code here is disposable — it answers the question, it is not the product.

## Setup

- Vite + React + TypeScript (disposable scaffold)
- `@open-e2ee/signal-protocol-sdk` v2.0.2 — AGPL-3.0, project is open source so this is compatible
- `idb` (required peer for the web IndexedDB store adapter)
- Two clients in one page: **Alice** (in-memory store), **Bob** (`indexedDbStore` — the persistence target)
- `inMemoryRelay` stands in for the real backend; envelopes are exchanged through it

## How to run

```bash
cd spikes/spike-01-e2ee-pwa
npm install
npm run dev
```

On Android: run `npm run dev -- --host` and open the LAN URL in Chrome for Android.

### Test procedure

1. Press **Run benchmark** — creates both clients, runs X3DH session setup, sends 10 mixed zh/es messages, reports timings.
2. Reload the page, then press **Verify reload survival** — recreates Bob from the persisted IndexedDB store and decrypts a new message. This proves identity and session state survive reload.
3. `npm run build` and record bundle size from the output table.

## Criteria and results

| Criterion | Threshold | Result (Node smoke test) | Result (Brave, Android) | Verdict |
| --- | --- | --- | --- | --- |
| X3DH session setup | < 2000 ms | ~485 ms | 1759 ms | **PASS** |
| Encrypt+decrypt round-trip (avg) | < 100 ms | ~81 ms | 46 ms | **PASS** |
| Encrypt+decrypt round-trip (p95) | < 300 ms | n/a | 148 ms | **PASS** |
| Session survives page reload | yes | n/a (Node has no IndexedDB) | yes — post-reload decrypt 286 ms | **PASS** |
| Bundle size added by SDK | < 500 KB gzip preferred | see notes | ~720 KB gzip total build | **warning** |
| Runs in Chrome Android | yes | n/a | yes (Brave, Chromium) | **PASS** |

## Findings so far

- **Secure context is mandatory.** `crypto.subtle` is undefined over plain HTTP on a LAN IP; the SDK fails with "Failed to initialize Signal Protocol". On Android either use `adb reverse tcp:5173 tcp:5173` (localhost on the phone is a secure context) or enable `brave://flags/#unsafely-treat-insecure-origin-as-secure` (Brave) / `chrome://flags/...` for the origin. In production this is a non-issue — the PWA is served over HTTPS — but it is a hard requirement to note in the threat model: no HTTPS means no crypto.
- **Core exchange works.** X3DH + Double Ratchet + decrypt verified end-to-end on a real Android device: ciphertext envelope of ~3.5 KB for a short bilingual message; decrypted content matches exactly.
- **The web adapter needs `idb` as a peer dependency** — `npm install idb` or the build fails with a missing `openDB` export. Not documented prominently; add to setup notes.
- **`processIncomingEnvelope` does not consume the relay mailbox.** The caller must ack via `relay.markDelivered(envelope.id)` (or `client.markAsRead`), otherwise the same envelope is replayed on the next poll and the ratchet correctly throws `DuplicatedMessageError`. The subscription path does this automatically; manual pull does not.
- **TOFU identity-change detection works — and it fires in normal dev flows.** Bob's persisted store pinned Alice's identity from a previous run; when Alice regenerated keys, decrypt failed closed with `UntrustedIdentityError`. This is correct anti-MITM behavior. Recovery path: `client.acceptIdentityRotation(userId, error.identity)` then re-process the envelope — in the real app this maps to the "contact's key changed" confirmation UI (safety numbers, deferred to the hardening milestone).
- **Subscription path quirk (unresolved).** `startRelaySubscription()` consumed and acked envelopes but the `onMessageDecrypted` hook never fired on Android — no error hook either. The pull-based path (`getPendingMessages` + `processIncomingEnvelope` + `markDelivered`) works reliably and is what the benchmark measures. Investigate the subscription path against the real NestJS relay before relying on push delivery.
- **Reload requires re-registering the device on the relay.** `inMemoryRelay` is ephemeral, so after a page reload the fresh relay has no device record or prekey bundle; `registerDevice(userId, { deviceId })` accepts the persisted id, and `syncToServer` re-uploads the bundle. With a real backend this concern disappears (Postgres-backed relay state persists).
- **Bundle size is heavy.** Production build: ~1.9 MB minified (~720 KB gzip) including the SDK. The largest chunk (`tables-*.js`, ~1.1 MB) is protocol lookup data. Mitigation if this hurts: dynamic-import the crypto engine so it loads after first paint; PWA precaching absorbs repeat-load cost. Not a blocker but worth the tradeoff note.
- **IndexedDB store is a singleton DB** (`signal-protocol-storage`); two SDK clients in one page cannot each have their own IndexedDB store — hence Alice runs on memory store in this spike. For the real app this is fine (one identity per install).

## Notes for the real implementation

- Wrap all SDK calls behind the CryptoEngine port (ADR 0006) — the spike hits the SDK directly.
- Receive path: prefer pull-based processing (`getPendingMessages` → `processIncomingEnvelope` → `markDelivered`) until the subscription-hook quirk above is understood; the same code path serves both "offline catch-up" and "live delivery".
- Identity changes must surface as a user-facing confirmation, never auto-accepted — the spike auto-accepts only because Alice's keys are ephemeral by construction.
- Post-quantum (PQXDH/ML-KEM) is on by default in this SDK — the spike exercised it implicitly on a mid-range phone with no measurable problem; no extra work needed.
