# ADR 0005: Message storage and backup

- Status: Accepted
- Date: 2026-09-16

## Context

ADR 0002 established that the server stores ciphertext envelopes only while in transit — they are deleted on delivery or after a TTL. That leaves an open question: **where does the user's message history live?**

In an E2EE system the answer is constrained: the server cannot hold readable history (it has no keys), and it cannot hold decryptable ciphertext archives without breaking forward secrecy — old envelopes are encrypted under ratchet keys that the Double Ratchet intentionally discards. Message history therefore lives on the client. The question is how it is protected there, and what happens when the device is lost or a second device is added.

Drivers, in priority order:

1. **Privacy at rest** — extracting IndexedDB from a device must yield nothing readable.
2. **MVP simplicity** — one device, one store, no sync machinery.
3. **Honest UX** — the user must understand that history is device-bound.
4. **Backup path** — a future option to survive device loss without weakening E2EE.

## Options considered

### Option A: Local-first encrypted vault (no server history)

The PWA keeps the full message history in IndexedDB, encrypted at rest under a vault key derived from the user's passphrase (PBKDF2/Argon2-equivalent via WebCrypto → AES-GCM). Cached translations are stored in the same vault — re-translating a message never re-calls the provider.

**Pros:**

- Strongest privacy: history exists only where the user physically holds it.
- Zero server storage growth; the database stays a pure relay.
- Simplest possible MVP: one store, one device, no sync conflicts.
- Translation caching falls out naturally — saves quota and avoids re-disclosing plaintext.

**Cons:**

- Device loss = history loss. The passphrase re-derives the key but there is nothing to decrypt without a backup.
- A second device starts empty; old envelopes cannot be re-decrypted anyway (ratchet keys discarded by design).

### Option B: Encrypted cloud backup (future)

The client exports an encrypted blob — history plus vault-encrypted key material, AES-GCM under a key derived from the user's passphrase (WhatsApp-E2EE-backup style). The blob can be uploaded to our server (which stores it as opaque bytes) or exported by the user to their own storage. Restore = fetch blob + enter passphrase.

**Pros:**

- Survives device loss while keeping the server blind — the blob is ciphertext to us.
- Enables multi-device seeding later: restore backup on device 2, then receive fresh envelopes per-device.
- User-controlled: optional, exportable, deletable.

**Cons:**

- Passphrase strength becomes the entire security of the backup — weak passphrases are brute-forceable offline since the blob is out of our control once exported.
- Requires a well-defined vault serialization format and careful handling of ratchet state (backup must capture enough state to continue sessions, or sessions must be re-established post-restore).

### Option C: Server-side ciphertext archive (rejected)

Keep delivered envelopes on the server indefinitely; new devices re-download and re-decrypt them.

**Rejected.** Fundamentally conflicts with forward secrecy: re-decrypting old envelopes requires retaining ratchet chain keys that the protocol deliberately destroys. The workable version of this idea (re-encrypting history under the vault key and storing that) is just Option B with extra steps — and it inflates server storage with data the server can never use.

## Decision

**Option A for the MVP — local-first encrypted vault, history dies with the device. Option B is the planned follow-up, and the vault format is designed now so backup lands cleanly later.**

MVP implementation:

- Message history, contacts metadata, and cached translations live in IndexedDB, encrypted at rest under a key derived from the user's passphrase (PBKDF2-SHA256, ≥600k iterations, via WebCrypto — Argon2id noted as desirable but impractical in browsers today).
- The vault key is held in memory while the app is unlocked; never persisted in cleartext.
- The settings UI states plainly: "Your history lives only on this device."
- The vault's serialization format is specified in the E2EE ADR so that Option B can wrap the same bytes into a backup blob without a migration.

Planned follow-up (post-MVP):

- "Export encrypted backup" in settings: vault serialized → AES-GCM under passphrase-derived key → uploaded as an opaque blob to our server or downloaded by the user.
- Restore flow: import blob + passphrase → vault reconstructed.
- Documented tradeoff in the UX: backup security equals passphrase strength.

## Consequences

### Positive

- The privacy promise extends to rest: device, server, and backup (when it lands) all hold only ciphertext or passphrase-gated material.
- Server stays minimal — no history storage, no sync service, matching ADR 0002's small-footprint Postgres.
- Translation cache in the vault reduces provider calls and repeated plaintext disclosure.

### Negative and mitigations

- **Device loss wipes history.** Mitigation: stated plainly in UX; Option B is the designed escape hatch.
- **No multi-device history in MVP.** Accepted: new devices see only messages sent after their registration.
- **Passphrase is the linchpin.** A forgotten passphrase locks the vault permanently. Mitigation: strength meter (zxcvbn-style) at setup, and the future backup flow inherits the same constraint — it is the accepted E2EE cost model.

### Revisit triggers

Re-evaluate if: user feedback makes device-loss recovery a top complaint (accelerates Option B); multi-device support enters scope (forces the backup/seed design sooner); or browser-native Argon2id becomes viable, upgrading vault key derivation.
