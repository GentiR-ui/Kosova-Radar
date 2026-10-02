# Blockers

> Things Claude could not finish alone. The loop keeps going on everything else.
> Each entry: what, why, evidence, what the human needs to do. Remove the entry when resolved.

<!--
## B-001 — Stripe keys missing (T020)
- Needed: Stripe__SecretKey, Stripe__WebhookSecret in the environment.
- Done meanwhile: checkout flow built against Stripe test mode with a fake gateway in tests.

## B-002 — Acceptance test REQ-004 contradicts requirements
- Evidence: test expects badge "Late"; REQ-004 says "Overdue". test-author confirmed the test is wrong.
- Human: fix the test and run scripts/approve-spec.sh --amend "REQ-004 badge text".
-->

## B-001 — test-author prompt still describes REQ-P04 as a cross-tenant check
- Evidence: `.claude/agents/test-author.md` says "REQ-P04: one cross-tenant test per tenant-scoped endpoint". ADR-001 removed tenancy; REQ-P04 is now the role check (Reviewer → 403 on Admin-only endpoints, data unchanged).
- Done meanwhile: the spec-phase delegation to test-author states the new meaning explicitly.
- Human: update that line in `.claude/agents/test-author.md` (human-owned) so later test-author runs (disputes) use the right meaning.
