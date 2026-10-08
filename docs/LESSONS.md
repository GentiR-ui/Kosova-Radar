# Lessons

> Written by the release skill after each project. Anything that should change the starter kit
> itself (a better default, a missing platform requirement, a hook that misfired) goes here,
> tagged [kit], so it can be copied back into the starter.

<!--
- [kit] Platform needs password reset as REQ-P12 — every product asked for it.
- [kit] verify.sh: npm audit flagged a dev-only package; switched to --omit=dev.
- [product] Users expected CSV import on day one.
-->

## Kosova Radar
- [kit] The whole-codebase review and QA each found an unbounded cache (login throttle), logging that did not meet the observability NFR (scopes off, EF SQL at Information), and a missing static-file caching/compression default: add them to the stack template.
- [kit] Add `overflow-wrap:anywhere` to the base CSS and a long-text 360 px check to the QA flows: ingested text is untrusted.
- [kit] verify.sh does not run prettier or the E2E flows in `--quick`; formatting drift accumulated (19 files).
- [kit] Exclude `appsettings.Development/Testing.json` from the production publish.
- [product] Add a confirm/undo path for destructive admin actions earlier; it conflicts with locked acceptance tests once approved.
