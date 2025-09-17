/*
Quickstart:
- make restore
- make build
- make test

Assumptions:
- Simulated AI client is acceptable for local development.
- Prompt datasets are stored in configuration and can be overridden via environment variables.

Limitations:
- AI client is a stub and does not call an external provider.
- Only a single sample dataset is bundled by default.
*/
# BazarBin MCP Server

## Overview

This solution provides a layered, testable .NET 8 API that serves AI-ready prompts enriched with reusable table schema metadata. The `/api/v1/prompt/{datasetId}` endpoint composes dataset prompts with schema information and returns simulated AI responses.

## Project Structure

- `BazarBin.Domain` – domain models for prompts and table schemas.
- `BazarBin.Application` – use-cases, contracts, and abstractions.
- `BazarBin.Infrastructure` – configuration-backed repositories, schema providers, and AI client implementations.
- `BazarBin.Mcp.Server` – Fast minimal API host with versioning and problem details.
- `BazarBin.Tests` – unit and integration tests using xUnit, FluentAssertions, and NSubstitute.

## Scripts

Use the provided `Makefile` for common workflows:

- `make restore` – Restore NuGet dependencies.
- `make build` – Build all projects.
- `make test` – Run unit and integration tests with coverage.
- `make lint` – Run formatting checks.
- `make format` – Apply formatting.
- `make start` – Launch the API locally.

## Configuration

Configuration follows the 12-factor pattern and can be supplied via `appsettings*.json` or environment variables. Copy `.env.example` to `.env` to override defaults locally.

## Running Locally

```bash
make restore
make build
make start
```

Swagger UI is available at `https://localhost:5001/swagger` in development.

## Testing

```bash
make test
```

## CI

GitHub Actions workflow in `.github/workflows/ci.yml` runs lint, build, and tests on every push.

## API Contract

### `GET /api/v1/prompt/{datasetId}`

Response body:

```json
{
  "datasetId": "sales-insights",
  "originalPrompt": "Summarize the latest sales performance and highlight anomalies.",
  "schemaComment": "/* Table Schema ... */",
  "finalPrompt": "/* Table Schema ... */\n\nSummarize ...",
  "aiResponse": "Simulated answer from simulated:gpt-sim for dataset sales-insights."
}
```

Errors follow RFC 7807 `application/problem+json` with an `errorCode` extension.

## Limitations & Future Work

- Replace the simulated AI client with an HTTP-based implementation using retry policies.
- Extend schema support to load from external metadata services.
- Add authentication and rate limiting for production environments.
