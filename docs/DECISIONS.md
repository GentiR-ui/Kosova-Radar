# Decisions (ADRs)

> Every decision the user did not make explicitly. Newest last. Keep each one short.
> Format below. Status: accepted | superseded by ADR-xxx.

<!--
## ADR-001 — Use Hangfire for scheduled invoice reminders
- Date: 2026-01-15 · Status: accepted · Task: T014
- Context: REQ-006 needs a daily job that survives restarts.
- Options: BackgroundService + timer (no persistence) · Hangfire with PostgreSQL storage · Quartz.NET
- Decision: Hangfire — persistent, retry built in, dashboard; reuses the existing PostgreSQL.
- Consequences: one extra package; dashboard must be behind the Owner policy.
-->
