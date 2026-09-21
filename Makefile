.PHONY: fixtures contract-test test backend-dev frontend-dev db migrate

# Regenerate canonical wire fixtures from shared/protocol (run after any protocol change)
fixtures:
	cd shared/protocol && npm run fixtures

# Validate C# mirror types against the generated fixtures
contract-test:
	cd backend && dotnet test tests/LetsChat.ContractTests

# Full suite: contract + unit + integration (integration needs Docker)
test:
	cd backend && dotnet test

backend-dev:
	cd backend/src/LetsChat.Api && dotnet run

frontend-dev:
	cd frontend && npm run dev

db:
	docker compose up -d postgres

# Apply EF Core migrations to the running database
migrate:
	cd backend && dotnet ef database update --project src/LetsChat.Infrastructure --startup-project src/LetsChat.Api
