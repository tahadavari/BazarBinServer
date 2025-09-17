.SILENT:

SOLUTION := BazarBin.sln

restore:
dotnet restore $(SOLUTION)

build: restore
dotnet build $(SOLUTION) --no-restore -c Release

lint:
dotnet format --verify-no-changes

format:
dotnet format

start: build
dotnet run --project BazarBin.Mcp.Server/BazarBin.Mcp.Server.csproj --no-build --urls http://0.0.0.0:5000

start-dev:
dotnet run --project BazarBin.Mcp.Server/BazarBin.Mcp.Server.csproj --no-build

coverage:
dotnet test $(SOLUTION) --no-build --collect:"XPlat Code Coverage" --results-directory ./TestResults
echo "Coverage results available under ./TestResults"
test -f TestResults/**/coverage.cobertura.xml && echo "Cobertura report generated" || true

TEST_ARGS ?=

test: restore
dotnet test $(SOLUTION) --no-build --configuration Release $(TEST_ARGS)

.PHONY: restore build lint format start start-dev coverage test
