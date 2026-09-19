# Let's Chat

End-to-end encrypted chat PWA with opt-in, high-quality translation (Chinese ↔ Spanish focus). The server is a dumb relay — it only ever sees ciphertext.

Product spec and architecture decisions live in `docs/` (requirements, decisions log, ADRs 0001–0006, C4 diagrams).

## Layout

| Path | What | Stack |
| --- | --- | --- |
| `frontend/` | Web PWA client (desktop-first, Android-installable) | Vite + React + TypeScript, vite-plugin-pwa |
| `backend/` | Dumb relay — envelopes, prekeys, contacts | ASP.NET Core (.NET), raw WebSocket, hexagonal (ports/adapters) |
| `shared/protocol/` | Wire-protocol types — client source of truth | TypeScript, compiled with tsc |
| `spikes/` | Disposable validation harnesses | see each spike's README |

Two separate projects (`frontend/`, `backend/`), each with its own toolchain and deploy unit. The client consumes `shared/protocol` directly; the .NET backend mirrors the same wire types as C# models, kept honest by contract validation tests (ADR-0001, revised 2026-09-18).

## Dev quickstart

```bash
# shared protocol (build once, rebuild when types change)
cd shared/protocol && npm install && npm run build

# backend — http://localhost:5000 (GET /health), ws://localhost:5000/relay
cd backend && dotnet restore && dotnet run

# frontend — http://localhost:5173
cd frontend && npm install && npm run dev

# postgres (needed once the persistence adapter lands — ADR-0002)
docker compose up -d postgres
```

## Conventions

- Hexagonal layout in `backend/`: `Domain/` and `Application/` never reference ASP.NET Core or EF Core; `Infrastructure/` holds adapters (HTTP endpoints, WebSocket middleware, EF Core stores).
- Commit style: conventional commits (`feat(scope): subject`), see `/commit` skill.
- Secrets never enter the repo — `.env` files are gitignored.
