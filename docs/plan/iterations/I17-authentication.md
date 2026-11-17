# I17 — Authentication (Identity)

| | |
|---|---|
| **Status** | todo |
| **Depends on** | I09, I02 |
| **Parallel with** | I11, I16 |
| **Branch** | `iter/I17-auth` |
| **Spec** | [01 Requirements §Users](../../spec/01-requirements.md#users-usr), [10 Security](../../spec/10-ops-security.md#4-security-checklist) |
| **Requirements** | FR-USR-1..3 |

## Goal
Users can register and log in. Cart, orders, library and conversations belong to the account. Admin endpoints require the admin role.

## Tasks
- [ ] ASP.NET Core Identity with Identity API endpoints (cookie auth for the SPA), schema `identity`.
- [ ] Anonymous → user merge on login (cart, conversations, orders).
- [ ] `IOwnerContext` returns the user id when authenticated.
- [ ] Admin role seeded from config in Development; admin policy on admin endpoints.
- [ ] Web: login / register pages, user menu, protected library page.
- [ ] Tests: merge on login, IDOR checks for orders and conversations.

## Acceptance criteria
- [ ] Anonymous cart survives login.
- [ ] Another user cannot read my order or conversation (test).
