# Release candidate — Kosova Radar (2026-10-05)

## What was built
Every REQ in `docs/requirements.md` (platform REQ-P01..P11, product REQ-001..REQ-114) has an automated acceptance test
under `tests/App.AcceptanceTests` (API) or `web/e2e/acceptance` (UI, desktop + mobile), locked by `scripts/approve-spec.sh`.
`scripts/verify.sh` (full) checks them together with the integration, unit, E2E-flow and audit gates and the
`traceability` step (every REQ has a test). Result of the last full run: see the top of `docs/PROGRESS.md`.

[manual] checks (done by the `qa` agent, desktop 1280, Pixel 7, 360 px): no horizontal overflow, AA contrast, visible focus,
keyboard-only login/reject/logout, loading/error/empty states. REQ-114 confirmed. The live Anthropic path (B-001) is **not** verified.
T042 (client-specific source adapters) is not built: blocked (B-002).

## Run locally
`docker compose up -d --wait db`, then `dotnet run --project src/App.Api` (Development: dev admin, synthetic fixtures) and
`cd web && npm run dev`. Full gate: `./scripts/verify.sh`.

## Deploy
Follow `docs/RUNBOOK.md` (single VPS, `docker-compose.prod.yml`, nightly `pg_dump`, restore steps). Required env vars:
`POSTGRES_PASSWORD`, `SITE_BASE_URL`, `Bootstrap__AdminEmail`/`Bootstrap__AdminPassword` (first start only),
`Proxy__KnownProxies` or `Proxy__KnownNetworks` (start-up refuses without), optional `Ai__ApiKey`. Migrations run at start-up.
Terminate HTTPS at the reverse proxy; the app sets HSTS, compresses responses and caches `/assets/*` for a year.

## Human checklist before going live
- [ ] Secrets set; bootstrap admin password rotated (RUNBOOK) and the bootstrap variables removed.
- [ ] Domain + TLS, proxy IP/network configured, backups enabled and one restore rehearsed on the server.
- [ ] B-001: run the live Anthropic smoke test once, or keep `Ai__Provider=None`.
- [ ] B-002: name the cleared sources and written permission, then T042.
- [ ] Privacy/terms pages if you collect personal data (only staff accounts are stored in V1).
- [ ] Re-run Lighthouse against the production container behind the real proxy (T041 measured `vite preview`).
- [ ] Analytics to measure the success signal in requirements §1.

## Known limitations (accepted for V1)
- "Tërhiq", "Skado" and "Çaktivizo" are one click with no confirmation; withdrawn items and deactivated users cannot be restored (ADR-032).
- A candidate that got `AiUnavailable` is not re-enriched when its source content is unchanged (ADR-029 wording is optimistic):
  after adding an AI key, old pending candidates keep empty AI fields until the source content changes.
- `/admin/sources` is not paginated; date filter inputs use the browser's locale format; focus does not return to
  "Refuzo" after Escape in the reject dialog.
- Leftover LOWs listed in `docs/PROGRESS.md` (T038–T041, T090) and the release review (Development returns 500 for
  unparsable query values; Production returns 400).
