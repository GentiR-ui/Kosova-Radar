# Requirements — <FILL: product name>

> WHAT the product must do. Every requirement is one testable EARS sentence with an id.
> LOCKED after `scripts/approve-spec.sh`; only the human can amend it.
> If a requirement can't be turned into a failing test, it is a wish — rewrite it or tag it `[manual]`.

## 1. Product

- **Problem:** <FILL: one sentence — whose pain, how often, what it costs them>
- **Target users:** <FILL: primary persona>
- **Value proposition:** <FILL: why they would pay>
- **Success signal for the market test:** <FILL: e.g. 10 paying teams within 30 days of launch>

## 2. Scope

**In (MVP):** <FILL: 3–7 capabilities>

**Out (explicitly not now):** <FILL: things people will ask for that we will not build yet>

## 3. EARS patterns

| Pattern | Template |
|---|---|
| Event | WHEN <trigger> THE SYSTEM SHALL <response> |
| State | WHILE <state> THE SYSTEM SHALL <response> |
| Unwanted | IF <bad condition> THEN THE SYSTEM SHALL <response> |
| Optional | WHERE <feature is enabled> THE SYSTEM SHALL <response> |
| Ubiquitous | THE SYSTEM SHALL <response> |

## 4. Platform requirements (every SaaS — keep unless the product truly differs)

- **REQ-P01** WHEN a visitor submits a valid registration (email, password of at least 12 characters, organization name) THE SYSTEM SHALL create a new organization (tenant), create the visitor as its owner, and sign them in.
- **REQ-P02** IF login credentials are invalid THEN THE SYSTEM SHALL respond 401 with the same generic message whether or not the email exists.
- **REQ-P03** IF an unauthenticated request targets any `/api` endpoint other than register and login THEN THE SYSTEM SHALL respond 401.
- **REQ-P04** IF an authenticated user reads, changes or deletes a resource that belongs to another organization THEN THE SYSTEM SHALL respond 404 and SHALL leave the data unchanged. (Applies to every tenant-scoped endpoint in design.md.)
- **REQ-P05** IF a request body fails validation THEN THE SYSTEM SHALL respond 400 with RFC 9457 problem details that name each invalid field.
- **REQ-P06** IF more than 10 login attempts for the same email arrive from the same client IP within one minute THEN THE SYSTEM SHALL respond 429 to the excess attempts.
- **REQ-P07** WHEN `GET /health` is called and the database is reachable THE SYSTEM SHALL respond 200.
- **REQ-P08** THE SYSTEM SHALL send the headers `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY` and `Referrer-Policy: strict-origin-when-cross-origin` on every response.
- **REQ-P09** IF an unhandled error occurs THEN THE SYSTEM SHALL respond 500 with problem details that contain no stack trace or internal exception text.
- **REQ-P10** WHEN a new visitor registers through the web app THE SYSTEM SHALL show the signed-in home screen with the organization name, on desktop and on a phone-sized screen.
- **REQ-P11** WHEN a signed-in user logs out THE SYSTEM SHALL end the session so that subsequent `/api` requests respond 401.

## 5. Product requirements

<!--
Write them like this. Group by capability. Number 001, 002, ... and never reuse a number.

### Invoices
- **REQ-001** WHEN a member creates an invoice with a customer, at least one line item and a due date THE SYSTEM SHALL save it as Draft and show it in the invoice list.
- **REQ-002** IF an invoice has no line items THEN THE SYSTEM SHALL reject it with a validation error on `lineItems`.
- **REQ-003** WHEN the owner marks a Draft invoice as Sent THE SYSTEM SHALL record the sent date and prevent further edits to its line items.
- **REQ-004** WHILE an invoice is overdue THE SYSTEM SHALL show it with an "Overdue" badge in the list.
- **REQ-005** The invoice PDF layout matches the brand guide. [manual]
-->

<FILL: product requirements, numbered from 001>

## 6. Non-functional

- Pages are usable at 360 px width and keyboard-navigable; form fields have labels.
- p95 API latency under 300 ms for list/detail endpoints with 10k rows per tenant.
- Personal data stays in the EU region; users can delete their account (GDPR). (adjust or delete)
