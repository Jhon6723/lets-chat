# ADR 0006: E2EE protocol and key management

- Status: Accepted
- Date: 2026-09-16

## Context

Every previous ADR assumed end-to-end encryption; this one defines it. The requirements accumulated so far:

- All message encryption/decryption happens client-side; the server is a dumb relay holding ciphertext envelopes with TTL (ADRs 0001, 0002).
- The envelope must carry the sender's declared language **inside the ciphertext** (ADR 0003).
- Message history lives in a local encrypted vault; its serialization format must be defined here so future encrypted backups work without migration (ADR 0005).
- Primary platform is a desktop web PWA (Android install supported) — a browser runtime where private keys must be generated, stored, and used via WebCrypto/IndexedDB, with no secure enclave or OS keychain.
- MVP scope: **1:1 chats, single device per user**. Groups and multi-device are explicitly deferred (each is a major protocol increment).

Drivers, in priority order:

1. **Do not roll our own crypto.** A student-built ratchet is a liability; the goal is a well-specified protocol, correctly integrated.
2. **Runs in a real browser.** Pure-TS or WASM; no native binaries; IndexedDB-backed session/key storage.
3. **Forward secrecy + post-compromise security** — the properties that justify calling the product E2EE.
4. **Auditability of the design** — we must be able to document and reason about the envelope and key lifecycle.
5. **License compatibility** with the project's distribution model.

## Options considered

### Option A: Signal Protocol profile via a TypeScript implementation

X3DH for session establishment, Double Ratchet for per-message keys, AES-256-GCM for message encryption, prekey bundles served by the relay. Implementation via `@open-e2ee/signal-protocol-sdk` (pure TypeScript, browser store over IndexedDB, adds PQXDH/ML-KEM post-quantum support) with `signal-wasm` (libsignal compiled to WASM, adds Kyber/PQXDH) as fallback.

**Pros:**

- Battle-tested protocol design — the reference for 1:1 E2EE, with published specifications for every component (X3DH, Double Ratchet, Sesame for multi-device later).
- Pure-TS/WASM builds run in the PWA today; no native toolchain.
- Post-quantum variants (PQXDH) available in both candidate libraries — a genuine differentiator vs mainstream apps.
- Active maintenance; the old `libsignal-protocol-javascript` is archived (2021) and excluded.

**Cons:**

- `signal-protocol-sdk` is `0.x` — public API may change before 1.0; browser store is marked experimental. Mitigation: pin versions, wrap behind our own CryptoEngine port (hexagonal fit), treat the ratchet as a replaceable adapter.
- License: AGPL-3.0 (commercial license available). **Resolved: the project is open source, so AGPL is compatible.** If the distribution model ever changes to closed/commercial, the CryptoEngine port is the seam that lets the SDK be replaced (e.g. by signal-wasm) without touching the domain — this was an explicit motivation for the hexagonal choice in ADR 0001.
- Not wire-compatible with Signal Messenger — irrelevant here, we run our own relay.

### Option B: MLS (RFC 9420) via ts-mls

The IETF-standard group key-agreement protocol, implemented in TypeScript (MIT license, actively maintained, WebCrypto-native ciphersuite).

**Pros:**

- IETF standard with a formal architecture RFC; MIT license is maximally permissive.
- The right answer for **groups** — tree-based rekeying scales where pairwise ratchets don't; multi-device is native to the model.

**Cons:**

- Designed for group state machines, not simple 1:1 chat — operational complexity (commits, proposals, welcome messages, delivery-service ordering guarantees) is disproportionate for a 1:1 MVP.
- Younger ecosystem for browser messaging apps; fewer reference integrations to copy.

### Option C: Custom minimal protocol on WebCrypto primitives

Hand-rolled X3DH-like ECDH + HKDF + AES-GCM, possibly with a simplified symmetric ratchet.

**Pros:** zero dependencies, full control, small code size.

**Cons — disqualifying:**

- Rolling a bespoke ratchet is exactly the "don't roll your own crypto" failure mode: skipped-key handling, out-of-order delivery, and session desync are where hand-rolled E2EE dies. The project's credibility claim (better privacy than WeChat) cannot rest on unreviewed cryptography.
- No external test vectors or conformance kit.

## Decision

**Option A: a Signal Protocol profile (X3DH + Double Ratchet + AES-256-GCM) via `@open-e2ee/signal-protocol-sdk`, wrapped behind a CryptoEngine port.**

MLS (Option B) is not rejected — it is **deferred to the groups milestone**, where it becomes the natural choice. The MVP stays 1:1/single-device.

### Key lifecycle (binding spec)

- **Identity**: each device generates a long-term identity keypair at registration. Private key stored in IndexedDB as a non-extractable CryptoKey where WebCrypto supports it; session/ratchet state lives in the encrypted vault (ADR 0005).
- **Prekeys**: each device publishes a signed prekey + a batch of one-time prekeys to the relay. The relay is a *contact-gated* public-key directory — bundles are served only to accepted contacts, and one-directionally to the target of a pending contact request (revised 2026-09-21, see decisions log). It never sees private material.
- **Session setup**: X3DH against the recipient's fetched bundle, per device.
- **Message encryption**: Double Ratchet → per-message AES-256-GCM keys; envelopes carry ratchet header + in-ciphertext language hint (per ADR 0003). Old chain keys are destroyed per the ratchet — which is also why Option C of ADR 0005 was rejected.
- **Delivery**: per-device envelopes; server deletes on ack or TTL expiry.

### Scope deferred (explicitly)

- **Multi-device**: post-MVP; requires Sesame-style device coordination and per-device sessions.
- **Groups**: post-MVP; MLS (Option B) is the designated candidate.
- **Post-quantum (PQXDH)**: the chosen SDK supports it; enabling it is a config-level decision once the classic profile is stable.
- **Safety numbers / identity verification UI**: required for a credible E2EE claim but deferred to the hardening milestone, tracked in the backlog.

### Known browser-runtime risks (accepted, documented)

- Same-origin JavaScript can reach IndexedDB records — XSS is the E2EE killer in a PWA. Mitigations are non-negotiable: strict CSP, minimal third-party scripts, dependency pinning, service-worker review.
- Non-extractable CryptoKeys protect the private key material itself, but ratchet session state is still JS-readable at runtime — an accepted browser limitation, stated in the threat model.
- AGPL-3.0 on the SDK: confirmed compatible — the project is open source (owner decision, 2026-09-16).

## Consequences

### Positive

- Credible, spec-backed E2EE without inventing cryptography.
- The CryptoEngine port keeps the hexagonal rule intact — protocol internals stay behind an interface, swappable if the SDK's API or our needs shift.
- Clear migration path: 1:1 Signal profile now, MLS for groups later, PQXDH as an upgrade flag.
- Wire-protocol types shared as a single TypeScript package on the client side; the .NET backend mirrors them as C# models kept honest by contract validation tests (ADR 0001 revised — the cross-language trade-off).

### Negative and mitigations

- **Alpha-grade dependency.** Mitigation: version pinning, conformance-style integration tests around the CryptoEngine port, `signal-wasm` documented as fallback.
- **Single-device only at launch.** Accepted; new devices see only post-registration messages (consistent with ADR 0005).
- **No history restore across devices.** Consistent with ADR 0005's local-vault decision; encrypted backup (its Option B) is the designed escape.

### Revisit triggers

Re-evaluate if: the SDK's license blocks the chosen distribution model; group chat enters scope (→ MLS evaluation); a maintained 1.0 alternative appears; or a security audit flags the browser store's experimental status as a release blocker.

## Spike validation (spike-01, 2026-09 — real device)

Validated on a mid-range Android phone in Brave (Chromium) against the disposable harness in `spikes/spike-01-e2ee-pwa/`:

- X3DH session setup: 1759 ms for both clients (threshold < 2000 ms).
- Encrypt + deliver + decrypt round-trip: 46 ms avg, 148 ms p95 over 10 mixed zh/es messages.
- IndexedDB persistence: session and identity survive a full page reload; post-reload decrypt 286 ms.
- Post-quantum path (PQXDH/ML-KEM) is enabled by default in the SDK and ran without issues — this ADR previously treated it as a later config flag; the spike shows it is simply on.

Integration quirks discovered (recorded in the spike README):

- `processIncomingEnvelope` does not consume the relay mailbox; the caller must ack with `markDelivered` or the ratchet correctly rejects the replayed envelope as a duplicate.
- TOFU identity-change detection fails closed (`UntrustedIdentityError`); recovery is `acceptIdentityRotation` behind user confirmation — this is the hook where the safety-number UI will attach later.
- The `startRelaySubscription` push path consumed envelopes without firing `onMessageDecrypted` on Android; the pull path works reliably. To be re-validated against the real relay backend before relying on push.
