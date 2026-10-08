# Client brief — Kosova Radar: Automated Data Collection Agent

> Received 2026-10-08 from the client (Gentian Racaj). Reference document for spec amendment v2
> (ADR-034..038). Where this brief and docs/requirements.md / docs/design.md differ, the spec and the ADRs win.
> Client decisions on 2026-10-08: use Gemini (key supplied), keep manual approval for now (ADR-005),
> B-003 resolved by Claude (ADR-036, ADR-037).

## Goal
Build an automated ingestion agent that collects Kosovo events and deals from configured public sources, extracts
structured information, validates it, detects duplicates, and stores it in the database. New sources must be
addable later without changing the core ingestion pipeline.

## V1 sources
- **Prishtina.com Events** — https://prishtina.com/events — upcoming events: title, description, date, start time,
  end time, venue, city, category, image, event URL, source name, source URL. Missing fields stay null.
- **Ka' Po Dalim** — https://kapodalim.com/ks — Kosovo events: title, description, date, time, venue, city,
  category, image, event URL, source name, source URL. Normalize its cities/categories into ours.
- **Future sources:** do not hard-code the system around these two. Configurable Source (id, name, baseUrl, type,
  enabled, fetchInterval, lastFetchedAt) and one adapter/parser per source when necessary.

## Pipeline
SOURCE → FETCH → EXTRACT RAW CONTENT → NORMALIZE → AI STRUCTURED EXTRACTION → VALIDATION → DUPLICATE CHECK →
CONFIDENCE → AUTO-PUBLISH OR ADMIN REVIEW → DATABASE

1. **Fetching:** fetch, parse HTML, remove navigation, footer, scripts, styles, ads and unrelated content, identify
   the event/deal cards, extract clean text and useful links/images. Only then send relevant content to the AI.
2. **Source adapter:** an interface like `fetch()`, `extractItems()`, `normalizeItem()`; e.g. PrishtinaEventsAdapter,
   KapodalimAdapter. The core ingestion service must not know any site's HTML structure.
3. **AI extraction:** provider abstraction; Gemini for V1 (cost). One cleaned item at a time; STRICT JSON
   (title, description, date, startTime, endTime, venue, city, category, price, currency, imageUrl, sourceUrl,
   confidence). Never invent missing information: null.
4. **Validation:** title required, valid date, not in the past for new events, known Kosovo city where possible,
   valid URL, valid price format, allowed category. Never trust AI output blindly.
5. **Duplicates:** source + sourceUrl; normalized title + date + venue; title/date/venue similarity. Existing event →
   update, not a second row.
6. **Confidence:** 90–100 % auto-publish, 70–89 % pending review, < 70 % rejected/needs review; thresholds
   configurable. Admin sees extracted data, source URL, confidence, AI result and validation errors.
   *(Client decision 2026-10-08: keep manual approval for now — ADR-005, ADR-038.)*
7. **Scheduled execution:** every 1–3 hours, enabled sources only, process only new/changed content, store
   lastFetchedAt, content hash and lastProcessedAt; never call the AI again on identical content.
8. **Manual run:** Admin → Sources → source → "Run now", showing fetched, new, updated, duplicates skipped,
   AI processed and failed items.
9. **Error handling:** one failing source never stops the others; log source, timestamp, error, HTTP status and
   item counts; Admin sees the latest source status.
10. **Database:** Sources (id, name, url, type, enabled, lastFetchedAt, lastSuccessfulRunAt, lastError, createdAt);
    Events (id, title, description, date, startTime, endTime, venue, city, category, imageUrl, sourceId, sourceUrl,
    confidence, status Pending/Published/Rejected, createdAt, updatedAt).
11. **Source attribution:** every imported event keeps its source and original URL; show "Source" / "View original".
12. **Do not build:** internet-wide discovery, multi-agent architecture, vector DB, RAG, embeddings, browser swarm,
    Kubernetes, microservices, message queues, agent frameworks, continuous AI reasoning, AI-generated events,
    AI deciding an event exists without source evidence.
13. **Principle:** the agent works only on configured sources — deterministic and observable.
14. **Adding sources:** add a Source record, implement an adapter if necessary, map fields, test with fixture data,
    enable the source — without modifying the core pipeline.
15. **Development:** fixtures for both sources first; the whole pipeline must work on fixtures before live fetching.
16. **Source policy:** sources configurable and disabled by default until permission is confirmed; never bypass
    authentication, CAPTCHAs, access controls or rate limits; no anti-bot circumvention; reasonable request rates
    and caching.

## Acceptance criteria (V1 complete when)
1 Admin configures sources · 2 Prishtina.com adapter · 3 Ka' Po Dalim adapter · 4 fetch source content ·
5 extract event candidates · 6 AI → structured JSON · 7 validation · 8 duplicate detection · 9 confidence stored ·
10 high-confidence auto-publish *(deferred: manual approval, ADR-005)* · 11 low confidence in Admin Review ·
12 manual run · 13 failed sources do not stop others · 14 update instead of duplicate · 15 source URL preserved ·
16 a third source without rewriting the core pipeline.
