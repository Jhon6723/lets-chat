.PHONY: fixtures contract-test backend-dev frontend-dev db

# Regenerate canonical wire fixtures from shared/protocol (run after any protocol change)
fixtures:
	cd shared/protocol && npm run fixtures

# Validate C# mirror types against the generated fixtures
contract-test:
	cd backend && dotnet test tests/LetsChat.ContractTests

backend-dev:
	cd backend/src/LetsChat.Api && dotnet run

frontend-dev:
	cd frontend && npm run dev

db:
	docker compose up -d postgres
