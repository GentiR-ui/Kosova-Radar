# Runbook — Kosova Radar (single VPS)

## Layout
`docker-compose.prod.yml` runs three containers: `app` (API + built SPA on :8080, bound to 127.0.0.1 only), `db`
(PostgreSQL 17, volume `db-data`) and `backup` (nightly `pg_dump -Fc` into volume `backups`). TLS is **not** terminated
here: put Caddy or nginx on the host in front of `127.0.0.1:8080` (the auth cookie is `Secure`, so plain HTTP does not log in).
Set the proxy's read timeout to at least 300 s (nginx `proxy_read_timeout 300s;`, default 60 s): "Merr tani" on an HTML source
with AI spacing can take minutes. The run itself continues if the proxy gives up; the result then shows in the source's run history.

## First start
1. `cp .env.example .env.prod`, then set the values below. Never commit it.
2. `docker compose -f docker-compose.prod.yml --env-file .env.prod up -d --build --wait`
3. Check `curl -i http://127.0.0.1:8080/health` → 200, then sign in at `SITE_BASE_URL/admin/login`.
4. Remove `BOOTSTRAP_ADMIN_*` from `.env.prod` and recreate the app (`up -d app`). V1 has no password change: if the bootstrap
   password was ever shared, create a new Admin under "Përdoruesit" with a fresh password, sign in as it and deactivate the bootstrap admin.

**Start one instance only.** Migrations, bootstrap admin, reference-data seed and the interrupted-run recovery all run at
start-up without a lock; never start two app containers against the same database at the same time (also not during a rolling restart).

## Environment variables (`.env.prod`)
| Variable | Required | Meaning |
|---|---|---|
| `POSTGRES_PASSWORD` | yes | database password (compose refuses to start without it) |
| `SITE_BASE_URL` | yes | public URL, e.g. `https://radar.example.com` (crawler User-Agent, SEO URLs, sitemap) |
| `BOOTSTRAP_ADMIN_EMAIL`, `BOOTSTRAP_ADMIN_PASSWORD` | first start only | creates the first Admin when no user exists; start-up fails if none is set on an empty DB. The documented development credentials (`admin@kosovaradar.local` / dev password) are refused in Production |
| `PROXY_KNOWN_PROXIES` / `PROXY_KNOWN_NETWORKS` | yes (start-up fails without one) | IPs / CIDRs whose `X-Forwarded-For`/`-Proto` are trusted. A host proxy reaches the container from the Docker bridge gateway: find it with `docker network inspect <project>_default` and use e.g. `172.18.0.0/16`. Without it every visitor shares one IP (rate limits, login throttle) and HSTS is not sent |
| `AI_PROVIDER`, `AI_API_KEY` | optional | a key alone enables AI extraction with Gemini (default model `gemini-3.5-flash-lite`); `Anthropic` + key selects Claude; empty/None = off. `Fake` is refused in Production. `AI_MODEL` overrides the model; `AI_MIN_INTERVAL_MS` (about 4000 on the Gemini free tier) spaces calls and must stay below `Ai__TimeoutSeconds` × 1000 because the wait counts against the call timeout. **Upgrading from the Anthropic-only version:** a key alone now selects Gemini, so keep Claude with `AI_PROVIDER=Anthropic` and an empty or `claude-*` `AI_MODEL` |
| `BACKUP_HOUR_UTC` (02), `BACKUP_KEEP_DAYS` (14) | no | backup schedule and retention |

More tunables (`Ingestion__*`, `RateLimit__*`, `Login__*`, `Ai__DailyCallLimit`) are listed in `.env.example`; add them to the `app`
environment in the compose file if you need to change them.

## Migrations
The app applies pending EF Core migrations at every start, before anything else; a failed migration stops the container
(`docker compose logs app`). Deploying a new version = rebuild and `up -d --build app`. Take a manual backup first (below).

## Sources and ingestion
Sources are configured in the admin UI (`/admin/sources`): type, URL, interval (15–1440 min), then tick "Aktiv". "Merr tani" runs one fetch.
Fixture sources are disabled in Production (`Ingestion__AllowFixtureSources=false`); synthetic fixture seeding is off. Only add sources
the client has confirmed in writing (ADR-006); the crawler honours robots.txt and a per-host delay.
HTML sources (type "Faqe HTML", ADR-035) need an adapter ("Prishtina.com — evente", "Ka' Po Dalim", "Foleja — oferta" or "Gjirafa Mall — oferta"). With `Seed__KnownSources=true` (default) all four are
created at start-up when no source has their URL, inactive and without auto-publish unless `Seed__KnownSourcesActive` / `Seed__KnownSourcesAutoPublish`
are true (existing rows are never changed; the two shop parsers are placeholders until T054/T055, so their runs fail with `MalformedPayload`); an inactive source stays disabled until an Admin ticks "Aktiv" or uses "Merr tani". The operator is
responsible for the sites' terms of use. A run that finds no listing fails with `MalformedPayload`: the site layout probably changed and its parser needs an update.

Auto-publish (ADR-039): a source with "Publiko automatikisht" publishes its new `Valid` candidates, and changes whose only review issue is
`ChangedSinceApproval`, without a person; the audit log shows them as `candidate.autoPublish` by the user `system`. Everything doubtful
(`UnknownCity`, `PossibleDuplicate`, `AiUnavailable`, …) stays in the review queue; "Tërhiq" on the published list withdraws a wrong item. The upgrade
renames `Ingestion__HtmlHorizonDays` (30) to `Ingestion__EventHorizonDays` (3): events of HTML and auto-publish sources starting on a local date
more than 3 days after the fetch date are skipped (counted in `received` only) and taken by a later fetch. Auto-published deals must be daily and at least
`AutoPublish__MinDealDiscountPercent` (35) off; other valid deals wait for a person (ADR-042).

Shop deals (ADR-045): "Foleja — Deals" and "Gjirafa Mall — Oferta" list products on sale without an end date. For an auto-publish source such a
deal is "today's deal" (public text "Oferta e sotme"): it is published when it is at least 35 % off, stays active while a successful fetch (every
3 h) still lists it, and disappears from the public site `Deals__ListingStaleHours` (6, 4–168; keep it above twice the poll interval) hours after the last fetch that listed it. Only deals with at least the minimum discount get this window. A fetch that lists
it with less than 35 % off ends it at once (it stays `Published` but is no longer active); when the discount comes back the same item is active again. A deal a person expired ("Skado") is never republished by a fetch. Listing deals are not
marked `Expired` by the expiration job when they merely stop being listed: they reappear on the first fetch that lists them again. HTML sources
follow the site's own "next page" links up to `Ingestion__HtmlMaxPages` (5) pages per run, same host only, robots.txt checked; a later page that
cannot be read ends the listing and keeps the pages already read. Shop deals never go to the AI.

Auto-publish is conservative by design: it does not backfill (candidates that are already pending stay pending until their content changes), it never
overrides a person's rejection, applies a change only to an item the same source published, requires the item link to be on the source's own host,
leaves candidates whose date, city or prices came from the AI to a person, and stops after `AutoPublish__MaxPerRun` (100) items per run (a warning is
logged). A person's edit of a published item is overwritten when the source changes it again (use "Tërhiq" to stop that).
In `docker-compose.prod.yml` the settings are `INGESTION_EVENT_HORIZON_DAYS`, `AUTO_PUBLISH_MIN_DEAL_DISCOUNT_PERCENT`, `AUTO_PUBLISH_MAX_PER_RUN`,
`SEED_KNOWN_SOURCES_ACTIVE` and `SEED_KNOWN_SOURCES_AUTO_PUBLISH` (the `.env.example` names are the application's own).

## Bootstrap-admin rotation
Bootstrap only acts on an empty user table. To rotate: create a new Admin in `/admin/users`, sign in as it, deactivate the old one.
If every Admin is locked out: stop the app, `docker compose exec db psql -U app -d app`, and deal with the `AspNetUsers` row manually, or restore a
backup; there is no password-reset e-mail.

## Backup and restore
- Nightly dumps: `docker compose -f docker-compose.prod.yml exec backup ls -l /backups` (custom format, `app-<UTC>.dump`, kept 14 days).
- Manual: `docker compose -f docker-compose.prod.yml exec backup sh -c 'pg_dump -Fc -f /backups/app-manual-$(date -u +%Y%m%dT%H%M%SZ).dump'`
- Copy off the machine regularly (the volume lives on the same disk as the database): `docker cp <backup-container>:/backups ./backups-copy`.
- Restore (replaces the data; ~minutes of downtime):
  ```
  docker compose -f docker-compose.prod.yml stop app
  docker compose -f docker-compose.prod.yml exec backup sh -c 'dropdb --force app && createdb app && pg_restore -d app --no-owner /backups/<file>.dump'
  docker compose -f docker-compose.prod.yml start app
  ```
- Rehearsed locally in T043 (dump of a populated DB, restore into a fresh one, app healthy, data present). Rehearse again on the real VPS before go-live.

## Health and monitoring
`GET /health` (anonymous) checks the database; the compose healthcheck uses it, and the host proxy / an uptime monitor should too.
Logs: `docker compose logs -f app`. The admin dashboard (`/admin`) shows source health and ingestion state.

## Known limitations
- ASP.NET Data Protection keys (cookie/antiforgery) are stored **unencrypted** in the database. Anyone with a DB dump can read them, so treat
  backups as secrets. Rotating means deleting the keys table rows (all sessions end).
- Single instance only (see First start); no horizontal scaling.
- Backups are on the same host by default; off-host copies are a manual step.
- AI calls go straight to the provider (no HTTP proxy, no redirects); a host behind an egress proxy cannot reach it. On the Gemini free tier Google may use the prompts (public feed text) to improve its products.
- The Gemini live smoke test is still outstanding (BLOCKERS B-001, `bash scripts/ai-smoke.sh`); first live AI call per schema may time out once.
- HTML sources (Prishtina.com, Ka' Po Dalim) depend on the sites' markup: a layout change makes runs fail with `MalformedPayload`
  (source health "Me probleme"/"Dështon") until the parser is updated against a new saved page.
