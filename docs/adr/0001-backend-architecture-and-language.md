# ADR 0001: Backend architecture and language

- Status: Accepted (revised 2026-09-18: backend changed from NestJS to ASP.NET Core)
- Date: 2026-09-16 (revised 2026-09-18)

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

**Use ASP.NET Core (C#, .NET) structured as a hexagonal architecture, with raw WebSocket transport and EF Core for persistence.**

Original decision (2026-09-16) was NestJS on Node.js — revised on 2026-09-18 by the product owner before any real backend code existed, making the switch cost essentially zero.

The revised deciding factors, in order:

1. **Developer experience.** The solo developer's production stack is .NET (EF Core, ASP.NET Core), stronger than TypeScript/Node. For a single-developer academic project, writing the backend in the stack the author knows best outweighs marginal technical factors.
2. **Hexagonal fit.** ASP.NET Core's built-in DI with interface registrations maps ports-and-adapters directly — application services depend on interfaces resolved by the container, adapters register implementations. NestJS's default idiom is the modular monolith; ports-and-adapters is achievable but fights the framework's grain (decorators, module boundaries, metadata magic).
3. **Accepted trade-off: shared protocol types are lost.** The original #1 factor — defining the E2EE wire format once in a shared TypeScript package — cannot survive a cross-language backend. Mitigation chosen (Option B, decided 2026-09-18): the protocol is duplicated by hand between the TS `shared/protocol` package (client source of truth) and C# models, kept honest by **contract validation tests** — fixtures generated from the TS package are deserialized by the .NET models and compared field-by-field in CI. Schema-based codegen (JSON Schema/TypeSpec → TS + C#) was evaluated and deferred: the protocol surface is small (~6 types) and stable; a contract test is sufficient insurance without codegen tooling.
4. **Raw WebSocket over SignalR.** The relay contract is deliberately minimal — deliver envelopes, ack them, fetch pending. SignalR would wrap our messages in hub framing, require its client library in the PWA, and its headline feature (automatic reconnection) does not solve our actual hard problem: E2EE reconnect requires application-level resync (pending-envelope fetch, ratchet session continuity, re-ack) which must be built regardless. Raw `UseWebSockets` middleware keeps the wire format identical to the shared protocol types — 1:1, no nested framing.
5. **EF Core + PostgreSQL** for persistence (per ADR 0002), running in Docker on the same VPS.

## Consequences

### Positive

- Backend written in the developer's strongest stack — faster iteration, fewer framework-fighting bugs.
- Idiomatic hexagonal fit via ASP.NET Core DI: domain/application layers depend on interfaces; infrastructure (WS middleware, controllers, EF Core adapters) registers implementations.
- Mature migration story (EF Core migrations) and testing ecosystem (xUnit, Testcontainers for Postgres integration tests).
- Top-tier runtime performance per instance — comfortable headroom for a dumb relay.

### Negative and mitigations

- **Protocol drift risk.** Wire types now exist twice (TS for the PWA, C# for the server). A divergence bug means undecryptable messages. Mitigation: contract validation tests — `shared/protocol` generates JSON fixtures; the .NET test suite asserts its models deserialize them field-for-field and serialize back identically. Drift is caught in CI, not compile time. If drift bugs slip through repeatedly, upgrade to schema-based codegen is the documented escalation path.
- **Two toolchains.** The repo now carries npm (frontend + shared/protocol) and dotnet (backend) toolchains. Accepted: they are independent projects anyway.
- **Framework leakage risk.** EF Core attributes and ASP.NET attributes can seep into the domain. Mitigation unchanged: domain and application layers import nothing from ASP.NET Core or EF Core; enforced by dependency-boundary tests (e.g., ArchUnitNET-style rules).

### Revisit triggers

Re-evaluate this decision if: sustained concurrent connections exceed ~20k per instance and a dedicated relay service is introduced (a Go/Elixir relay can sit behind the same application ports); the contract tests repeatedly fail to catch drift (escalate to schema codegen); or the team grows beyond the solo developer with members stronger in another candidate stack.
