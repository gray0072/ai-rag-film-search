# ADR-0007 — Frontend stack: npm, shadcn/ui + Tailwind, OpenAPI-generated client

- **Status:** Accepted
- **Date:** 2026-11-17
- **Iteration:** I02

## Context
Spec 02 fixes React 19 + TypeScript + Vite, React Router, TanStack Query and i18next, and leaves three choices to I02: the package manager, the component library and the way API types are generated. The UI has a store part (I10) and a chat part (I13) with streaming markdown, tool results and confirmation cards. The API contract is the OpenAPI document of the ASP.NET Core API; DTO types must not be written by hand (AGENTS.md).

## Options

**Package manager**
1. **npm**: comes with Node, one less tool for contributors and CI, supported by `AddViteApp` by default, workspaces are not needed (one app).
2. pnpm: faster installs and a stricter `node_modules`; needs Corepack or a separate install.

**Component library**
1. **shadcn/ui + Tailwind CSS v4 (Radix primitives)**: components are copied into the repo (`src/components/ui`) and are our code; accessible Radix primitives; the most common base for AI chat UIs (message lists, markdown, prompt inputs), which helps I13. Cost: more styling decisions, components are updated by the CLI, not by a package bump.
2. Mantine: a complete package with many ready components (forms, notifications, date pickers) and less styling work. Cost: a large runtime dependency with its own styling system; chat UI building blocks are less common.

**API client**
1. **openapi-typescript + openapi-fetch + openapi-react-query**: types only (no generated runtime code), a 6 kB fetch wrapper, typed TanStack Query hooks (`api.useQuery("get", "/api/info")`), middleware for headers.
2. orval: generates hooks and clients as code, many options (mocks, Zod). Cost: more generated code to review and keep in sync.

**Where the OpenAPI document comes from**
1. **Generated on build** (`Microsoft.Extensions.ApiDescription.Server`) into `src/web/openapi/v1.json` and committed. `npm run gen:api` reads that file. CI checks that both the document and the TypeScript types are up to date.
2. Downloaded from the running API (`/openapi/v1.json`). Cost: the API (and the database) must be running to regenerate types, and Node does not trust the ASP.NET Core dev certificate.

## Decision
npm; shadcn/ui (Radix base, `new-york` style, neutral colors) with Tailwind CSS v4; openapi-typescript + openapi-fetch + openapi-react-query; the OpenAPI document is generated on every API build and committed.

## Consequences
- An API change shows up as a diff in `src/web/openapi/v1.json` in the same PR; the frontend regenerates `src/api/schema.d.ts` with `npm run gen:api`. CI fails if either file is stale, so the contract cannot drift silently.
- Build-time generation runs `Program.cs` up to `builder.Build()` in the `GetDocument.Insider` process. Code that needs configuration (the database) is skipped there (`BuildTimeOpenApi.IsGeneratingDocument`). New modules that read required configuration at registration time must do the same or defer validation.
- New UI components are added with `npx shadcn add <component>` and reviewed like our own code. shadcn uses its `cn` package (class merging) instead of a local `clsx` + `tailwind-merge` helper.
- To switch to Mantine later, only `src/components/ui` and the Tailwind classes change; routing, data and i18n stay.
