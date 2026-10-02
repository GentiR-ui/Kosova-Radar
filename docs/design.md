# Design — <FILL: product name>

> HOW the product is built: the contracts the implementation must follow.
> The acceptance tests are written against sections 5 and 6, so keep them precise.
> Change after approval only together with an ADR in docs/DECISIONS.md.

## 1. Stack

Default stack (see docs/stack/dotnet-react.md). Deviations need an ADR.

| Concern | Choice |
|---|---|
| Frontend | React + TypeScript (Vite), TanStack Query, React Router, Tailwind |
| Backend | ASP.NET Core 10, minimal APIs |
| Architecture | Clean/Onion, 4 projects: Domain ← Application ← Infrastructure ← Api |
| Data | EF Core + PostgreSQL |
| Auth | ASP.NET Core Identity, cookie auth (same origin, SameSite=Strict, HttpOnly, Secure) |
| Multi-tenancy | Shared database, `TenantId` on every tenant-owned row, EF global query filter |
| Background jobs | none / `BackgroundService` / Hangfire (PostgreSQL storage) — <FILL: pick one> |
| Cache | none until measured need (then Redis via `IDistributedCache`) |
| Tests | xUnit (unit, integration, API acceptance) + Playwright (UI acceptance) |
| Delivery | One Docker image (API + SPA), GitHub Actions CI |

**Deviations:** none.

## 2. Architecture rules

- Domain: entities, value objects, domain rules. No framework references.
- Application: use cases (one class per use case or a small service per aggregate), DTOs, validation, interfaces for infrastructure (`IAppDbContext`, `IClock`, `IEmailSender`, ...).
- Infrastructure: EF Core `AppDbContext`, migrations, Identity, external services.
- Api: endpoint mapping only (thin), auth policies, problem details, rate limiting, hosting of the SPA.
- No MediatR/CQRS/repository-over-EF ceremony unless an ADR justifies it.

## 3. Tenancy & authorization

- Registration creates a `Tenant` and an owner `AppUser` with `TenantId`.
- Every tenant-owned entity implements `ITenantOwned { Guid TenantId }`; `AppDbContext` applies a global query filter and stamps `TenantId` on insert from `ITenantContext`.
- Reads of another tenant's id therefore return "not found" (404), never 403.
- Roles: `Owner`, `Member` <FILL: adjust to product>. Authorization policies per endpoint in section 5.

## 4. Domain model

| Entity | Key fields | Tenant-owned | Notes |
|---|---|---|---|
| Tenant | Id, Name, CreatedAt | — | the organization |
| AppUser | Id, Email, TenantId, Role | yes | Identity user |
| <FILL> | | | |

## 5. API contracts

All endpoints are under `/api`, JSON, camelCase. Errors are RFC 9457 problem details.
Validation errors: 400 with `errors: { field: [messages] }`. Missing or other-tenant ids: 404.

### Platform (fixed)

| Method | Path | Auth | Request | Responses | Req |
|---|---|---|---|---|---|
| POST | /api/auth/register | anonymous | `{ email, password, organizationName }` | 201 + auth cookie, body `MeDto`; 400; 409 email taken | P01, P05 |
| POST | /api/auth/login | anonymous, rate-limited per (client IP, email): 10/min | `{ email, password }` | 200 + auth cookie, body `MeDto`; 401 generic; 429 | P02, P06 |
| POST | /api/auth/logout | user | — | 204, cookie cleared | P11 |
| GET | /api/me | user | — | 200 `MeDto`; 401 | P03 |
| GET | /health | anonymous | — | 200 healthy; 503 unhealthy | P07 |

`MeDto = { userId: string, email: string, tenantId: string, organizationName: string, role: string }`

### Product

| Method | Path | Auth | Request | Responses | Req |
|---|---|---|---|---|---|
| <FILL> | | | | | |

## 6. UI map

| Route | Purpose | Key elements (accessible names used by tests) | Req |
|---|---|---|---|
| /register | sign up | inputs labelled "Email", "Password", "Organization name"; button "Create account" | P01, P10 |
| /login | sign in | inputs "Email", "Password"; button "Sign in" | P02 |
| / | home (signed in) | heading with organization name; button "Log out" | P10, P11 |
| <FILL> | | | |

## 7. Integrations

| Service | Purpose | Status |
|---|---|---|
| Payments (Stripe Checkout + webhooks) | subscriptions | off until a REQ needs it |
| Email (provider via `IEmailSender`) | invites, password reset | off until a REQ needs it |

## 8. Non-functional notes

- Logging: built-in structured logging; never log passwords, tokens or full request bodies.
- Migrations: EF Core migrations in Infrastructure; applied automatically in Development/Testing, via a deploy step in Production.
- Config: environment variables only; keys documented in `.env.example`.
