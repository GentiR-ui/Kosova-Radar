# Runbook — Kosova Radar (single VPS)

## Layout
`docker-compose.prod.yml` runs three containers: `app` (API + built SPA on :8080, bound to 127.0.0.1 only), `db`
(PostgreSQL 17, volume `db-data`) and `backup` (nightly `pg_dump -Fc` into volume `backups`). TLS is **not** terminated
here: put Caddy or nginx on the host in front of `127.0.0.1:8080` (the auth cookie is `Secure`, so plain HTTP does not log in).

## First start
1. `cp .env.example .env.prod`, then set the values below. Never commit it.
2. `docker compose -f docker-compose.prod.yml --env-file .env.prod up -d --build --wait`
3. Check `curl -i http://127.0.0.1:8080/health` → 200, then sign in at `SITE_BASE_URL/admin/login`.
4. Remove `BOOTSTRAP_ADMIN_*` from `.env.prod`, change the admin password in the UI if it was ever shared, and recreate the app
   (`up -d app`).

**Start one instance only.** Migrations, bootstrap admin, reference-data seed and the interrupted-run recovery all run at
start-up without a lock; never start two app containers against the same database at the same time (also not during a rolling restart).

## Environment variables (`.env.prod`)
| Variable | Required | Meaning |
|---|---|---|
| `POSTGRES_PASSWORD` | yes | database password (compose refuses to start without it) |
| `SITE_BASE_URL` | yes | public URL, e.g. `https://radar.example.com` (crawler User-Agent, SEO URLs, sitemap) |
| `BOOTSTRAP_ADMIN_EMAIL`, `BOOTSTRAP_ADMIN_PASSWORD` | first start only | creates the first Admin when no user exists; start-up fails if none is set on an empty DB. The documented development credentials (`admin@kosovaradar.local` / dev password) are refused in Production |
| `PROXY_KNOWN_PROXIES` / `PROXY_KNOWN_NETWORKS` | yes (start-up fails without one) | IPs / CIDRs whose `X-Forwarded-For`/`-Proto` are trusted. A host proxy reaches the container from the Docker bridge gateway: find it with `docker network inspect <project>_default` and use e.g. `172.18.0.0/16`. Without it every visitor shares one IP (rate limits, login throttle) and HSTS is not sent |
| `AI_PROVIDER`, `AI_API_KEY` | optional | `Anthropic` + key enables AI extraction; empty/None = off. `Fake` is refused in Production |
| `BACKUP_HOUR_UTC` (02), `BACKUP_KEEP_DAYS` (14) | no | backup schedule and retention |

More tunables (`Ingestion__*`, `RateLimit__*`, `Login__*`, `Ai__Model`, `Ai__DailyCallLimit`) are listed in `.env.example`; add them to the `app`
environment in the compose file if you need to change them.

## Migrations
The app applies pending EF Core migrations at every start, before anything else; a failed migration stops the container
(`docker compose logs app`). Deploying a new version = rebuild and `up -d --build app`. Take a manual backup first (below).

## Sources and ingestion
Sources are configured in the admin UI (`/admin/sources`): type, URL, interval (15–1440 min), then tick "Aktiv". "Merr tani" runs one fetch.
Fixture sources are disabled in Production (`Ingestion__AllowFixtureSources=false`); synthetic fixture seeding is off. Only add sources
the client has confirmed in writing (ADR-006, BLOCKERS B-002); the crawler honours robots.txt and a per-host delay.

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
- The Anthropic live smoke test is still outstanding (BLOCKERS B-001); first live AI call per schema may time out once.
- Site-specific source adapters (T042) are not built until the client names sources.
