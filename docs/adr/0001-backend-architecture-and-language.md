# ADR 0001: Backend architecture and language

- Status: Accepted
- Date: 2026-09-16

## Context

Let's Chat needs a backend that acts as a deliberately "dumb relay": it authenticates users, maintains WebSocket connections, stores and forwards encrypted envelopes, manages public prekeys, and exposes a small REST surface for contacts, devices, and delivery state. It never sees plaintext, which removes any need for server-side message processing and simplifies the security posture.

The frontend is already decided: Vite + React + TypeScript. The architect (solo developer, student project) has a stated preference for **hexagonal architecture (ports and adapters)**, so the chosen stack must support that style idiomatically rather than fighting it.

Non-functional drivers, in priority order:

1. **Real-time delivery.** Long-lived WebSocket connections are the core transport. Connection churn and moderate fan-out matter more than raw throughput.
2. **Idiomatic hexagonal fit.** Inversion of control, interface-driven application services, and framework adapters kept at the edges.
3. **Shared language with the frontend.** The E2EE protocol defines message envelopes, prekey bundles, and ratchet headers. Sharing TypeScript types between client and server eliminates a whole class of serialization bugs.
4. **Solo developer velocity.** Small team, academic context; onboarding cost and boilerplate matter.
5. **Deployment simplicity.** One container, one process, predictable resource usage on a cheap VPS.
6. **Performance headroom.** Nice to have, but the workload (relaying small ciphertext payloads) is I/O-bound and modest at the expected scale.

## Options considered

### Option A: TypeScript — Node.js with NestJS

NestJS is a TypeScript framework built around a dependency-injection container, modules, controllers/gateways, and providers — a structural cousin of Angular and Spring. WebSocket support is first-class through gateway decorators over ws or Socket.IO.

**Pros:**

- Native DI container makes ports and adapters idiomatic: domain services depend on injected interfaces, infrastructure lives in its own modules.
- Same language and toolchain as the frontend. E2EE envelope types, prekey bundle shapes, and protocol versioning can be shared from a common package, keeping client and server protocol definitions in sync automatically.
- WebSocket gateways are built in; no third-party plumbing required.
- Massive hiring/learning ecosystem; the developer already knows TypeScript from the frontend decision.
- Good default testing story (Jest/supertest) aligned with a hexagonal test pyramid.

**Cons:**

- Heavy framework: decorators and metadata magic can obscure the domain if discipline slips. Requires explicit layering rules to keep the framework out of core code.
- Node's single-threaded event loop is weaker for extreme connection counts than Go or BEAM. Acceptable here: benchmarks show Node holds thousands of concurrent connections fine, far above this project's realistic scale.
- More boilerplate than minimal frameworks (Express/Fastify/Hono).

### Option B: Java — Spring Boot

The enterprise standard, and the canonical home of hexagonal architectures in the JVM world.

**Pros:**

- Mature DI, mature WebSocket/STOMP support, enormous reference material for ports-and-adapters designs.
- Excellent testing ecosystem (Testcontainers, Spring Boot Test) and observability tooling.

**Cons:**

- Verbose; slowest iteration loop among the candidates for a solo developer.
- No language sharing with the TypeScript frontend: the E2EE protocol would be defined twice with drift risk.
- Highest memory footprint per instance; wasteful on a cheap VPS.

### Option C: Go

Compiled, minimal, with goroutine concurrency that excels at massive WebSocket workloads.

**Pros:**

- Best raw performance-to-memory ratio; tiny static binary deploys trivially to a VPS.
- Goroutines handle tens of thousands of concurrent connections comfortably.
- Language-agnostic hexagonal style is common in the community.

**Cons:**

- Hexagonal DI must be wired manually; doable and explicit, but adds boilerplate on every change.
- Protocol types duplicated across Go (server) and TypeScript (client), with drift risk on every crypto-envelope change — the most sensitive code path in the project.
- Steeper build/test scaffolding for someone whose primary language stack is TypeScript.

### Option D: Elixir — Phoenix

The BEAM platform is purpose-built for real-time messaging; Phoenix Channels are the industry's reference implementation of WebSocket fan-out.

**Pros:**

- Unmatched concurrency model for chat: hundreds of thousands of connections per node, per-process fault isolation.
- Channels + presence solve a large share of chat transport concerns for free.

**Cons:**

- Hexagonal architecture maps awkwardly onto Erlang/OTP idioms (applications, GenServers, supervision trees). Forcing ports-and-adapters here produces friction, not clarity.
- New language and paradigm for the developer; highest learning cost of all options.
- Same protocol-duplication problem as Go, plus a smaller ecosystem for E2EE key-delivery libraries.
- Overkill: solves a scale this project will likely never reach.

### Option E: C# — ASP.NET Core

High-throughput, mature DI, and excellent real-time support via SignalR.

**Pros:**

- Native DI and clean project boundaries support hexagonal design well.
- SignalR provides robust WebSocket transport with automatic fallbacks.
- Top-tier runtime performance, close to Go on benchmarks.

**Cons:**

- Protocol types duplicated with the TypeScript frontend.
- Heavier operational story than Node or Go for a single-service deployment.
- No relevant prior experience stated; slower ramp-up than Option A.

## Decision

**Use NestJS (TypeScript, running on Node.js with the Fastify adapter) structured as a hexagonal architecture.**

The deciding factors, in order:

1. **Shared protocol definitions.** In an E2EE system the envelope and key-bundle formats are the most correctness-critical surface in the entire project. Defining them once in a shared TypeScript package consumed by both the PWA client and the backend removes drift risk where a drift bug means undecryptable user messages. No other option offers this.
2. **Idiomatic hexagonal fit.** The stated architectural preference is directly served by NestJS's DI container and module system, without ceremony.
3. **Developer velocity.** One language end to end for a solo developer with an academic timeline.
4. Performance alternatives (Go, Phoenix) optimize for a scale this project does not have; Node comfortably covers the realistic load of relaying small ciphertext payloads.

## Consequences

### Positive

- Single language across the stack; shared protocol package lives in a monorepo workspace.
- Ports and adapters are enforceable through NestJS module boundaries and token-based injection.
- WebSocket gateways and guards cover connection authentication and room fan-out natively.
- The Fastify adapter keeps the HTTP layer fast while retaining the NestJS structure.

### Negative and mitigations

- **Framework leakage risk.** NestJS decorators make it easy to let framework concerns seep into the domain. Mitigation: enforce a package layout where the application and domain layers import nothing from NestJS packages; only adapter modules may depend on the framework. Verified with dependency-boundary linting.
- **Capacity ceiling.** Node's event loop saturates earlier than Go or BEAM under extreme concurrency. Mitigation: the hexagonal design keeps transport adapters replaceable. If connection scale ever becomes a constraint, a dedicated relay service (Option C or D) can be introduced behind the same application ports without reworking the domain.
- **Boilerplate weight.** Compared with minimal Node frameworks, NestJS is ceremony-heavy. Accepted knowingly in exchange for structural enforcement.

### Revisit triggers

Re-evaluate this decision if: sustained concurrent connections exceed ~20k per instance; the shared-package approach proves unworkable for crypto code that must also run in non-TS environments; or the team grows beyond the solo developer with members stronger in another candidate stack.
