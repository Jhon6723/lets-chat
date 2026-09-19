# ADR 0002: Persistence strategy — self-hosted PostgreSQL in Docker vs managed database service

- Status: Accepted
- Date: 2026-09-16

## Context

The backend (see ADR 0001) needs persistence for exactly these workloads:

- **Users and devices**: accounts, device registrations, public prekey bundles.
- **Encrypted envelopes**: opaque ciphertext stored for offline delivery, with short retention — messages are deleted once delivered or after a TTL.
- **Contact graph and delivery state**: contact requests, block lists, per-device ack status.

Because the product is end-to-end encrypted, the database holds no readable message content, which softens (but does not eliminate) data-protection requirements. Storage volume is small: ciphertext envelopes with a delivery TTL dominate, and they churn rather than accumulate.

A hard constraint shapes this decision: the backend needs **persistent WebSocket connections**, which rules out serverless platforms and short-lived compute for the API itself. The backend therefore already requires an always-on server — realistically a single VPS (e.g. Hetzner-class, roughly 4–8 EUR/month). The persistence question is whether the database rides along on that server or is rented as a separate managed service.

Non-functional drivers, in priority order:

1. **Cost.** Student budget; the realistic comparison is 0 EUR/month (co-located) vs 19–25 EUR/month (managed floor for an always-on relational database).
2. **Fit with deployment shape.** One docker-compose stack vs split infrastructure.
3. **Operational responsibility.** Backups, patching, monitoring — owned by us, or outsourced.
4. **Latency.** Co-located database means sub-millisecond round trips with no network egress cost.
5. **Vendor independence.** No lock-in to a platform's auth/realtime model, which would conflict with the custom E2EE protocol anyway.

## Options considered

### Option A: Self-hosted PostgreSQL in Docker on the application VPS

PostgreSQL runs as a container in the same docker-compose stack as the backend, with a named volume. Backups run as scheduled pg_dump jobs pushed to offsite object storage (S3-compatible, e.g. Backblaze B2).

**Pros:**

- Zero incremental infrastructure cost — the VPS is mandatory anyway for WebSockets.
- Sub-millisecond latency over the private Docker network; no egress fees; no per-MAU or storage metering.
- Full control over version, extensions, configuration, and data residency.
- The deployment unit stays simple: one compose file, one host, reproducible.
- Data that matters most (ciphertext) would look identical in any managed service — a managed layer cannot make it safer than good backup hygiene can.

**Cons:**

- We own backups, patching, and monitoring. A misconfigured backup job quietly failing is the classic failure mode.
- Single point of failure: if the VPS dies, both app and database are down until restore.
- Scaling means resizing or re-architecting the VPS; no push-button read replicas.

### Option B: Managed PostgreSQL — Supabase or Neon

Supabase is a backend-as-a-service built on Postgres (auth, realtime, storage included); Neon is serverless Postgres with scale-to-zero compute.

**Pros:**

- Backups, patching, failover handled by the provider.
- Free tiers exist for development (Supabase 500 MB; Neon 0.5 GB scale-to-zero).
- Supabase bundles auth, storage, and realtime subscriptions.

**Cons:**

- **Cost floor for production use:** Supabase Pro is 25 USD/month (and free-tier projects pause after 7 days of inactivity); Neon's always-on tier sits near 19–69 USD/month with cold starts below that. Both exceed the entire VPS that hosts the rest of the stack.
- **Architectural mismatch.** Supabase's value proposition (bundled auth, generated APIs, realtime row subscriptions) duplicates what our hexagonal backend and custom E2EE protocol must do themselves. Realtime row subscriptions are moreover a poor transport for E2EE envelopes: protocol events belong to the application layer, not to database change feeds.
- Cold starts (Neon below the Scale tier) degrade a real-time chat experience.
- Network latency and egress metering between VPS and managed DB add friction and cost.

### Option C: Managed NoSQL — Firebase (Firestore)

Document store with realtime listeners and generous free tier.

**Pros:**

- Realtime listeners could replace part of the WebSocket layer.
- Free tier covers meaningful early-stage usage.

**Cons:**

- **Severe lock-in:** data modeled around Firestore documents and listener semantics; migrating out later is a rewrite, not a port.
- Conflicts with ADR 0001: the hexagonal backend owns delivery logic and connection state; delegating realtime delivery to Firestore would hollow out the architecture.
- Query flexibility is weak for delivery-state and multi-device fan-out patterns (batch acks, per-device envelope copies, TTL pruning), which map naturally to relational SQL.
- The E2EE threat model prefers minimizing third-party custody of ciphertext and metadata.

## Decision

**Self-host PostgreSQL in a Docker container on the same VPS as the backend, orchestrated with docker-compose, with automated offsite backups.**

Rationale:

1. **Cost structure fits the project.** The database costs nothing beyond the VPS the app already needs; managed alternatives would multiply the monthly bill two- to five-fold for capabilities this architecture deliberately does not use.
2. **Architectural coherence.** A custom E2EE relay needs an application layer between the network and the data; platforms that collapse that layer (Supabase realtime, Firestore listeners) work against the design rather than for it.
3. **Data profile is forgiving.** Small working set, high churn, short retention — exactly the profile a modest co-located Postgres handles effortlessly.
4. **Lock-in avoided.** Plain PostgreSQL with EF Core migrations (per the ASP.NET Core backend, ADR 0001 revised) keeps every exit door open.

### Operational requirements made mandatory by this decision

Choosing self-hosting transfers these duties to the project; they are part of the decision, not afterthoughts:

- **Backups:** nightly pg_dump to offsite S3-compatible storage with retention (7 daily, 4 weekly), and a **quarterly documented restore drill** — an untested backup is not a backup.
- **TLS and network posture:** database reachable only on the Docker internal network; no exposed ports; TLS and HSTS on the public API.
- **Updates:** PostgreSQL pinned to a specific minor version, upgraded deliberately; compose stack updated monthly.
- **Monitoring:** basic alerting on disk usage, container health, and backup-job success.
- **Retention enforcement:** envelope TTL pruning runs in the application and is covered by tests, since it is a privacy guarantee, not just housekeeping.

## Consequences

### Positive

- Total early-stage infrastructure cost stays in single-digit EUR/month.
- Deployment is one compose file; any competent engineer (or the author) can reproduce or migrate it.
- No metering surprises (per-MAU auth billing, storage overages, egress) on any growth path this project realistically has.
- Full SQL power for delivery semantics: per-device envelope copies, batched acknowledgement updates, indexed prekey lookups, TTL deletion.

### Negative and mitigations

- **Backup responsibility.** Mitigated by the mandatory backup/restore-drill requirements above.
- **Single point of failure.** Accepted at this scale: downtime means users' undelivered messages wait, and nothing plaintext-sensitive is lost. Restore target documented as: new VPS + latest offsite dump + compose up, under one hour.
- **No managed failover or read replicas.** Accepted; see revisit triggers.

### Revisit triggers

Re-evaluate this decision if: traffic saturates a comfortably-sized VPS for two consecutive months; multi-region latency becomes a user complaint; restore drills reveal RTO exceeding four hours for real usage levels; or a second engineer joins who strongly prefers managed operations and the budget allows it.
