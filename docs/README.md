# Let's Chat — Documentation

An end-to-end encrypted chat application delivered as a Progressive Web App, with a built-in, high-quality translator as its core differentiator.

## Problem

Mainstream messaging apps fall short in two areas at the same time:

1. **Privacy.** Most popular messengers either do not offer end-to-end encryption by default or retain metadata and message-processing capabilities that weaken user privacy. Translation features typically require sending plaintext to a server.
2. **Translation quality.** Apps that integrate translation (for example, WeChat) produce low-quality translations for language pairs such as Chinese-Spanish. Users who communicate across those languages regularly need to copy messages into an external translator, breaking the flow of conversation and leaking private content to third-party services.

No mainstream app solves both problems simultaneously.

## Solution

Let's Chat is a web chat application, installable on Android as a PWA, that combines:

- **End-to-end encryption by default.** Messages are encrypted on the sender's device and decrypted only on the recipient's device. The server relays and stores only opaque ciphertext and cannot read message content.
- **Built-in, high-quality translation.** Received messages are decrypted on the client first, then translated, so plaintext never has to leave the device unencrypted to reach a chat server. The target quality bar is a significant, measurable improvement over WeChat for Chinese-Spanish.

## Target platform

- Primary: desktop web, installable as a PWA (offline shell, push-free messaging via WebSocket). Android install remains supported — the responsive PWA covers both.
- Secondary: desktop browsers.

## Current technical decisions

| Area | Decision | Reference |
| --- | --- | --- |
| Frontend | Vite + React + TypeScript, PWA | Settled during product definition |
| Backend | Pending — see ADR 0001 | [ADR 0001](./adr/0001-backend-architecture-and-language.md) |
| Persistence | Pending — see ADR 0002 | [ADR 0002](./adr/0002-persistence-strategy.md) |
| Translation engine | Pending — see ADR 0003 | [ADR 0003](./adr/0003-translation-engine.md) |

## Documents

- [Requirements (RF / RNF)](./requirements.md)
- [ADR 0001: Backend architecture and language](./adr/0001-backend-architecture-and-language.md)
- [ADR 0002: Persistence strategy](./adr/0002-persistence-strategy.md)
- [ADR 0003: Translation engine](./adr/0003-translation-engine.md)
- [ADR 0004: Translation provider catalog](./adr/0004-translation-provider-catalog.md)
- [ADR 0005: Message storage and backup](./adr/0005-message-storage-and-backup.md)
- [ADR 0006: E2EE protocol and key management](./adr/0006-e2ee-protocol.md)
- [Decisions log](./decisions.md)
- Diagrams: Structurizr DSL sources in `docs/diagrams/code/` (system context and containers), rendered images in `docs/diagrams/img/`

## Known design tensions

### Translation vs. end-to-end encryption

Translation is fundamentally incompatible with server-side processing of end-to-end encrypted content: the server only holds ciphertext. Therefore translation must happen **after decryption, on the client side**. ADR 0003 resolves this by calling an external provider (DeepL by default, user-configurable with bring-your-own-key) from the client, proxied through the backend, under a two-layer explicit consent model — on-device models were rejected to keep the PWA lightweight on mid-range Android.

For LLM-based providers (DeepSeek, OpenAI-compatible endpoints — post-MVP), the backend injects a strict translate-only system prompt owned by the app — the user never edits it. NMT providers (DeepL, Google, Azure) have no prompt surface and are configured purely through provider parameters.

Note: the Chrome built-in Translator API is not viable for the primary platform — as of Chrome 138 it is desktop-only and does not work on Android.

### Key management on the web

E2EE in a browser means private keys live in browser storage (IndexedDB, ideally non-extractable CryptoKeys). Device loss, multi-device support, and key backup all need explicit protocol decisions before implementation.
