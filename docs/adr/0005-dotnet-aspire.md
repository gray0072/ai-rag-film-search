# ADR-0005 — .NET Aspire for local orchestration and observability

> [Docs](../README.md) › [ADRs](README.md) › ADR-0005

- **Status:** Accepted
- **Date:** 2026-11-17
- **Iteration:** I00

## Context
Locally we run PostgreSQL, the API, the Worker, the React dev server, later maybe Langfuse. We want traces of LLM calls from the first day.

## Options
1. **.NET Aspire AppHost**: one command starts everything, connection strings are injected, the dashboard shows logs / traces / metrics (including GenAI spans), deployment path to Azure Container Apps through `azd`.
2. Docker Compose + separate OpenTelemetry collector + Jaeger/Grafana: more universal, more setup.

## Decision
Aspire for development. Dockerfiles / Compose for deployment are decided in I20 and do not depend on Aspire being present at runtime.

## Consequences
- Integration tests use Testcontainers directly (not the AppHost) to stay fast and isolated. `Aspire.Hosting.Testing` may be used for a few end-to-end tests.
- LM Studio runs outside Aspire (desktop app); the AppHost only passes its URL as configuration.

---

**Previous:** [ADR-0004](0004-payment-gateway-abstraction.md) · **Next:** [ADR-0006](0006-agent-own-loop-first.md) · **See also:** [All ADRs](README.md) · [Docs hub](../README.md)
