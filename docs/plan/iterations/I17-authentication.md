# I17 — Authentication (Identity)

| | |
|---|---|
| **Status** | done |
| **Depends on** | I09, I02 |
| **Parallel with** | I11, I16 |
| **Spec** | [01 Requirements §Users](../../spec/01-requirements.md#users-usr), [10 Security](../../spec/10-ops-security.md#4-security-checklist) |
| **Requirements** | FR-USR-1..3 |

## Goal
Users can register and log in. Cart, orders, library and conversations belong to the account. Admin endpoints require the admin role.

## Tasks
- [x] ASP.NET Core Identity with cookie auth for the SPA, schema `identity` (own endpoints instead of `MapIdentityApi`, see Result).
- [x] Anonymous → user merge on login (cart, conversations, orders).
- [x] `IOwnerContext` returns the user id when authenticated.
- [x] Admin role seeded from config in Development; admin policy on admin endpoints.
- [x] Web: login / register pages, user menu, protected library page.
- [x] Tests: merge on login, IDOR checks for orders and conversations.

## Acceptance criteria
- [x] Anonymous cart survives login.
- [x] Another user cannot read my order or conversation (test).

## Result

### What was done
- **Identity store:** `AppDbContext` now derives from `IdentityDbContext<AppUser, IdentityRole<Guid>, Guid>`. The tables are in schema `identity` (`users`, `roles`, `user_roles`, `user_claims`, `user_logins`, `user_tokens`, `role_claims`) with snake_case index names; migration `I17_AddIdentity`. The Identity schema version is fixed in the context (`Version2`), so the design-time and runtime models match. Options: email = user name and unique, passwords 8–128 characters without composition rules (NIST), lockout after 5 failures for 5 minutes.
- **Endpoints** (`Modules/Identity`): `POST /api/auth/register` (creates the account and signs it in), `POST /api/auth/login`, `POST /api/auth/logout`, `GET /api/auth/me` → `CurrentUser { authenticated, email, roles }`. Errors are Problem Details with `code`: `invalidCredentials` (unknown email and wrong password look the same), `lockedOut`, `emailTaken` (409). Password rules and email format return a 400 Validation Problem.
- **Cookie:** the Identity application cookie `fs_auth`: HttpOnly, Secure, SameSite=Lax, 14 days sliding. It answers 401/403 instead of redirecting.
- **Owner:** `OwnerCookieMiddleware` runs after `UseAuthentication`. It uses the user id when signed in, else `fs_sid`; `OwnerContext.IsAnonymous` tells the two apart.
- **Merge on login and register** (FR-USR-2): `IOwnerMerger` runs every module's `IOwnerMergeHandler` (Core contracts) in one transaction, before the cookie is issued.
  - Commerce: orders, entitlements and cart lines. For a movie in both carts the newer line wins. Lines blocked by the account's entitlements are dropped.
  - Assistant: conversations and checkout confirmations.
  - Only an anonymous request merges. `fs_sid` is deleted on login and logout.
- **Admin** (FR-USR-3): the policy `admin` (role `admin`) is on the whole `/api/admin` group (401 / 403); `Admin:Enabled` is removed. `AdminAccountSeeder` runs on startup under an advisory lock. It creates the role and the account `Auth:Admin:Email` (Development: `admin@filmsearch.local`) with `Auth:Admin:Password` from user secrets. An existing account gets the role only if the configured password matches. Without a password it logs a warning.
- **Web:**
  - `/login` and `/register` (one `AuthForm`; `returnUrl` limited to app paths, so no open redirect).
  - A user menu in the header (shadcn `dropdown-menu`: email, library, sign out), or a "Sign in" link.
  - `RequireAuth` on `/library`.
  - After login or logout the user goes into the cache and all other queries are refetched (the owner changed).
  - Texts in en / ru / sv.
- **Tests:**
  - Integration `AuthApiTests` (8):
    - register / me / logout / login, with the cart kept by the account;
    - refused registrations (taken email, case-insensitive; short password; invalid email);
    - the same answer for an unknown email and a wrong password, and lockout on the 5th failure, even for the right password afterwards;
    - **anonymous cart, paid order, library and conversation survive login**, and `fs_sid` is removed;
    - merge rules (newer line wins, owned movie dropped);
    - signing in as another user moves nothing;
    - **another user cannot read or pay my order, or read or write my conversation** (404, empty lists);
    - admin endpoints: anonymous 401, user 403, admin 200.
  - Existing admin tests now sign in through `CreateAdminClientAsync`.
  - Unit: the middleware with a signed-in user, and an unauthenticated identity that has a user id claim.
  - Web (6 new): login with the return URL and refetch of the account's cart; refused login in Russian; foreign return URL ignored; registration errors in Swedish (409 and 400); sign out from the menu; library → login → back.
- **Docs:** ADR-0010; spec 04 (owner identification, new "Auth" section, admin header), spec 03 (Identity), spec 02 (module list), spec 10 (checklist), README ("Admin account" and the endpoint list), AGENTS.md (auth test helpers).

### Manual checks
- **API** in Development from the worktree against a temporary pgvector container (port 55432) at `https://localhost:7299`. On startup it applied all migrations, including I17, and created `admin@filmsearch.local` from the user secret. curl:
  - anonymous: cart line and conversation created; `/api/library` 200; `/api/admin/embeddings` 401;
  - register → `Set-Cookie: fs_auth=…` and `fs_sid=; expires=1970…`; the cart line and the conversation are the account's; admin endpoint 403;
  - logout → `me` anonymous, empty cart;
  - admin login → roles `["admin"]`, admin endpoint 200.
- **Web** (Vite on 5199 with `API_HTTPS` pointing at that API, Playwright Chromium):
  - an anonymous visitor buys-to-cart a movie; `/library` → `/login?returnUrl=%2Flibrary`;
  - "Create an account" → back on `/library`, and the cart still has 1 movie;
  - the account menu shows the email; sign out → "Sign in" link and an empty cart;
  - a wrong password shows the message; the right password → the account's cart is back.
- The shared dev database (`filmsearch-postgres`) was not touched, because the I16 session used it.
- **Checks** (before the rebase onto I16): `dotnet build` (0 warnings), `dotnet format --verify-no-changes`, OpenAPI document stable, `dotnet test` 444/444; web `gen:api` (no diff), lint, format check, typecheck, tests 62/62, build. After the rebase onto I16: the I17 migration was regenerated on top of `I16_AddIngestionJobs` (only the identity tables), I16's job endpoints are under the admin policy (401 / 403 in the OpenAPI document), `WorkerTests` and the reindex job test sign in as admin; `dotnet test` 469/469, web 62/62, all other checks green.

### Decisions and deviations
- **Own endpoints instead of `MapIdentityApi`** (ADR-0010). The plan said "Identity API endpoints", but `MapIdentityApi` maps about ten endpoints we do not use (bearer tokens and refresh, 2FA, reset and confirmation mails without a mail sender). It has no logout, no place for the merge before the cookie, and no `code` in errors. Identity itself (store, hashing, lockout, security stamp, cookie) is used as is.
- **One DbContext** with the Identity tables, not a separate Identity context: one migration history, and the merge runs in one transaction.
- **The admin account is seeded wherever `Auth:Admin` is configured,** not only in Development. It is safe because of the password check for an existing account, and it gives I20 a way to create the admin with secrets. Development sets only the email.
- **`GET /api/library` stays owner-scoped and open to anonymous owners.** The movie page uses it for the "already yours" state, and anonymous purchases are still possible. The *page* requires sign-in, so purchases end up in an account.
- **CSRF:** SameSite=Lax cookies, and JSON-only bodies (a cross-site form cannot send `application/json`), no antiforgery tokens. Written in ADR-0010 and spec 10.
- **New package:** `Microsoft.AspNetCore.Identity.EntityFrameworkCore` 10.0.15 (Infrastructure). **New UI component:** shadcn `dropdown-menu` (on the existing `radix-ui`).

### Follow-ups
- Email confirmation and password reset (needs a mail sender; I20 or a stretch goal). Until then an email is only a login name.
- I19: rate limiting for `/api/auth/login` and `/register` (lockout protects one account, not password spraying across accounts).
- I20: `Auth:Admin:Email` / `Auth:Admin:Password` as deployment secrets; Data Protection keys must be persisted (database or volume), otherwise every restart signs everyone out.
- Worker: when it gets owner-scoped work (e.g. emails), keep the owner id from the job, never resolve it again.
