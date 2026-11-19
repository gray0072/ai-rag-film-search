# ADR-0010 — Authentication: ASP.NET Core Identity, own cookie endpoints, merge of anonymous data

> [Docs](../README.md) › [ADRs](README.md) › ADR-0010

- **Status:** Accepted
- **Date:** 2026-11-18
- **Iteration:** I17

## Context
Before I17 every visitor was an anonymous owner (`fs_sid` cookie). FR-USR-2 adds accounts with email and password, and the anonymous cart must survive login. FR-USR-3 requires an admin role for admin endpoints, which until now were mapped only in Development. The client is our own SPA on the same site (Vite proxy in development, one origin in deployment). There is no mail sender yet.

## Options
1. **ASP.NET Core Identity + `MapIdentityApi<TUser>()`** (the plan's first idea): ready-made endpoints. Cons: it maps about ten endpoints we do not use (bearer tokens and refresh, 2FA, password reset and email confirmation that need an `IEmailSender`). They would all be in the OpenAPI document and the attack surface. There is no logout endpoint. There is no place to run the anonymous merge before the cookie is issued. Errors have no `code` like the rest of the API.
2. **ASP.NET Core Identity (user store, password hashing, lockout, security stamp) + four own minimal API endpoints** (`register`, `login`, `logout`, `me`) on `UserManager` / `SignInManager` with the Identity application cookie.
3. **External identity provider** (Keycloak, Entra ID, Auth0) with OIDC. Production-grade, but it adds a service to run or an external account. Its learning value is about OIDC, not AI.
4. **JWT bearer tokens in the SPA.** Tokens in JavaScript storage are open to XSS. The SPA is same-site, so a cookie is simpler and safer.

## Decision
Option 2.
- **Store:** `AppDbContext` derives from `IdentityDbContext<AppUser, IdentityRole<Guid>, Guid>`. The tables are in schema `identity` with snake_case names, one migration history. The Identity schema version is fixed in the context (`Version2`, no passkeys table), so the design-time model and the runtime model are the same.
- **Cookie:** `fs_auth`: HttpOnly, Secure, SameSite=Lax, 14 days sliding. The handler answers 401/403 instead of redirecting. SameSite=Lax plus JSON-only bodies (a cross-site form cannot send `application/json`) is the CSRF protection; there is no antiforgery token.
- **Owner:** `OwnerCookieMiddleware` runs after authentication: the user id when signed in, else `fs_sid`.
- **Merge:** login and register first run `IOwnerMerger`, then issue the cookie, so a failed merge signs nobody in. It runs every module's `IOwnerMergeHandler` (Commerce: orders, entitlements, cart lines, where for the same movie the newer line wins and lines blocked by the account's entitlements are dropped; Assistant: conversations, confirmations) in one transaction. A module touches only its own tables. Only an anonymous request merges: signing in as B while signed in as A moves nothing. `fs_sid` is deleted on login and logout, so after logout the browser starts with a new, empty owner.
- **Passwords:** NIST SP 800-63B style: at least 8 characters (at most 128), no composition rules. Lockout after 5 failures for 5 minutes. Unknown email and wrong password give the same `invalidCredentials`.
- **Admin:** policy `admin` (role `admin`) on `/api/admin/*`. The role and the account `Auth:Admin:Email` / `Auth:Admin:Password` (password in user secrets) are seeded on startup under an advisory lock. An existing account with that email gets the role only if the configured password matches, so registering the admin email first does not make someone an admin. `Admin:Enabled` is removed.
- **Library:** `GET /api/library` stays owner-scoped (also for anonymous owners: the movie page shows "already yours"). The web library page requires sign-in.

## Consequences
- Anonymous shopping and anonymous orders still work; an account keeps them across devices.
- New owner-scoped data needs an `IOwnerMergeHandler`, or it stays with the anonymous id after login.
- Not done yet: email confirmation and password reset (they need a mail sender), external logins, 2FA, account deletion. The register endpoint tells that an email is taken (account enumeration). That is accepted for a store; rate limiting comes in I19.
- To reverse: Identity stays the user store; the endpoints can be swapped for `MapIdentityApi` or OIDC without changing the owner model.

---

**Previous:** [ADR-0009](0009-job-queue-in-postgresql.md) · **Next:** [ADR-0011](0011-ai-resilience-and-fallback.md) · **See also:** [All ADRs](README.md) · [Docs hub](../README.md)
