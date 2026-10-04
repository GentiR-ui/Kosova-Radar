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

## B-001 — Live Anthropic smoke test not run (T027)
- Needed: `Ai__ApiKey` (Anthropic API key) in the environment where tests run, and the production setting `Ai__Provider=Anthropic` (or just the key).
- Done meanwhile: `AnthropicAiProvider` is complete and covered by stubbed-HTTP unit tests (success, 429, 5xx, invalid JSON, refusal, schema violations). `AnthropicAiProviderTests.Live_smoke_test_against_the_real_API` is skipped without the key.
- Human: run `Ai__ApiKey=... dotnet test tests/App.UnitTests --filter Live_smoke` once; confirm it passes (the request shape, i.e. `output_config.format` on the configured model, is only verified against the real API there). Then remove this entry.
- Also check on the first live run: the request shape (`output_config.format` json_schema with `anyOf` nullables, the category `anyOf` with an enum) is accepted by the configured model, and the first call per schema (one per item kind, categories included) pays a grammar-compile latency that may exceed `Ai__TimeoutSeconds` (15) once; a timeout there only costs one `AiUnavailable`.

## B-002 — Client sources not named (T042)
- Needed: the 2–3 sources the client has cleared (URLs) and written confirmation that scraping/ingesting them is permitted (ADR-006).
- Done meanwhile: generic fixture/URL adapters, everything else in the plan.
- Human: name the sources and confirm permission; then T042 can start.
