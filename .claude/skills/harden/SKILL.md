---
name: harden
description: Security hardening checklist for the SaaS factory (authorization, tenant isolation, validation, secrets, dependencies, logging). Use for risky tasks and the full pre-release pass; the security-reviewer agent follows it.
---

# Hardening checklist

Run it on the diff of a risky task, or on the whole app before release (T090). Every item is checked by
reading the code and, where possible, by a test or a real request. Fix findings with a regression test.

## Access
- [ ] Every endpoint has `RequireAuthorization(...)` or a commented `AllowAnonymous()`; list all anonymous ones.
- [ ] Role/permission checks for owner-only actions are enforced server-side, not only hidden in the UI.
- [ ] Every tenant-owned entity implements `ITenantOwned`; the integration test proving every one has a query filter passes.
- [ ] No `IgnoreQueryFilters()`, raw SQL or `FromSql*` that bypasses tenant filtering.
- [ ] Ownership is never taken from client input (no `tenantId`/`userId` in request bodies used for writes).
- [ ] Cross-tenant acceptance tests exist for every tenant-scoped endpoint (REQ-P04).

## Input & output
- [ ] Every request record has validation (lengths, ranges, formats, required); collections have max sizes.
- [ ] No mass assignment: entities are never bound directly from requests.
- [ ] Lists are paginated with a max page size.
- [ ] Raw SQL only with parameters; no string-built SQL.
- [ ] No `dangerouslySetInnerHTML` with user content; URLs from users are validated (no `javascript:`).
- [ ] File uploads (if any): size limit, content-type allowlist, stored outside wwwroot, random names.
- [ ] Outbound HTTP to user-supplied URLs (if any) blocks private/internal addresses (SSRF).
- [ ] Error responses are problem details without stack traces or exception messages outside Development.

## Sessions & abuse
- [ ] Auth cookie: HttpOnly, Secure, SameSite=Strict; logout invalidates the session.
- [ ] Rate limits on login, registration, password reset and any expensive or email-sending endpoint.
- [ ] Passwords: Identity hashing, minimum length 12; no password or token in logs or URLs.

## Secrets & config
- [ ] No secrets in code, appsettings*.json, docker-compose for production, tests or git history (`verify.sh` secrets stage).
- [ ] Every required setting is documented in `.env.example`; the app fails fast at startup if one is missing in Production.

## Dependencies
- [ ] `dotnet list App.slnx package --vulnerable --include-transitive` is clean.
- [ ] `npm audit --omit=dev --audit-level=high` in web/ is clean.
- [ ] No unmaintained or unnecessary packages added.

## Logging
- [ ] Structured logging with message templates; user input never forms the template (log injection).
- [ ] No personal data beyond ids in logs; security events (failed logins, 403s, rate-limit hits) are logged.

## Headers & transport
- [ ] Security headers from REQ-P08 on every response; HSTS in Production.
- [ ] Forwarded headers configured when running behind a proxy.
