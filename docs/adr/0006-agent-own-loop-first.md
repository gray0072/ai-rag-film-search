# ADR-0006 — Own agent loop first, frameworks later

> [Docs](../README.md) › [ADRs](README.md) › ADR-0006

- **Status:** Accepted
- **Date:** 2026-11-17
- **Iteration:** I00

## Context
The assistant needs tool calling, streaming, confirmation steps and limits. Ready options: `FunctionInvokingChatClient` middleware in Microsoft.Extensions.AI, Microsoft Agent Framework, Semantic Kernel agents. The main goal of the project is learning.

## Decision
Write our own small loop on top of `IChatClient` (spec 06, section 2): send history + tools, stream text, run tool calls, append results, repeat with an iteration limit. Tool schemas are generated from C# methods with `AIFunctionFactory`, but invocation is ours.

After the assistant works and has evaluations (I14), a stretch task compares the own loop with `FunctionInvokingChatClient` or Microsoft Agent Framework on the same evaluation suite, and this ADR is updated.

## Consequences
- Full control over streaming events, confirmation flow, limits and logging, and a clear understanding of how agents work inside.
- More code to own and test. Edge cases (parallel tool calls, malformed arguments, models that write tool calls as text) are our problem → test cases.

---

**Previous:** [ADR-0005](0005-dotnet-aspire.md) · **Next:** [ADR-0007](0007-frontend-stack.md) · **See also:** [All ADRs](README.md) · [Docs hub](../README.md)
