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

## B-001 — Live Gemini smoke test (T046)
- Needed: the human runs the live smoke test once with the Gemini key from `.env` (Claude never reads `.env`).
- Done meanwhile: providers are covered by stubbed-HTTP unit tests; Development and Testing use the Fake provider.
- Human: after T046, run `bash scripts/ai-smoke.sh` (loads `.env`, runs `GeminiAiProviderTests.Live_smoke_test_against_the_real_API`). If it fails, paste the output into this entry. Then remove this entry.
