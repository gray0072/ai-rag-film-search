# I20 — Containers and deployment

| | |
|---|---|
| **Status** | todo |
| **Depends on** | I15, I17 |
| **Parallel with** | I19 |
| **Spec** | [10 Ops §5–6](../../spec/10-ops-security.md#6-deployment-i20) |
| **Requirements** | NFR-7, NFR-9 |

## Goal
The app runs as containers outside the dev machine (demo environment) with a cloud AI profile, built and deployed by CI.

## You will learn
Shipping an AI app: configuration per environment, secrets, migrations in deployment, costs of hosted models.

## Tasks
- [ ] Choose target (VPS + Docker Compose or Azure Container Apps through `azd`) → ADR.
- [ ] Dockerfiles (Api, Worker, Web static build behind a reverse proxy), `deploy/` folder.
- [ ] Migrations as a separate step / bundle.
- [ ] Production config: OpenRouter profile, Stripe test keys, CORS, HTTPS, prompt content logging off.
- [ ] CI: build images on tag, push, deploy. Playwright e2e smoke against the deployed app.
- [ ] Spending limits on the OpenRouter key; README section "Demo environment".

## Acceptance criteria
- [ ] Public demo URL works with the fake or Stripe test gateway.
- [ ] Rollback procedure documented.
