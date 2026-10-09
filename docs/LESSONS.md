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

### Release v2 (HTML sources, Gemini, bulk approve; 2026-10-09)
- [kit] Every HTML/DOM step needs a budget, not only the parse: the reviews found, in turn, quadratic listing discovery, quadratic element removal (20k cap now) and a recursive `Clone` that overflowed the stack on a 15 KB page (process crash, uncatchable). Add to the harden checklist: "no recursion over untrusted trees; cap element counts; one deadline over parse + clean + map; test with 5,000-deep nesting on a 1.5 MB-stack thread".
- [kit] Scraped sites show "today's occurrence" of running series (poster day, weekday, featured position): content hashes must be built from occurrence-free text, and parser contract tests should always include a two-day fixture pair. Add this to the `new-saas` design template for any HTML adapter.
- [kit] Client-side bulk actions over single-item endpoints must re-check the row (status + a version/`fetchedAt`) before acting, and must not swallow 401. Better default: a server-side precondition (`If-Match`/version) on approve endpoints in the stack template.
- [kit] `verify.sh` (full) fails when a dev server or a QA run holds :5080/:5173 or locks `src/App.Api/bin` (`CI` disables `reuseExistingServer`; MSBuild cannot copy the DLL). verify.sh should detect busy ports/locked outputs up front and say so, and the qa agent should build into a separate `--artifacts-path`.
- [kit] A full verify that spans machine sleep reports bogus failures (19 h acceptance run, DNS `Timeout` instead of `Unreachable`): the loop should keep the machine awake (`powercfg`/`caffeinate`) or re-run once on timing failures.
- [kit] `node -e`/heredoc edits lose backslashes in regexes on Git Bash; prefer the Edit tool for C# regex literals. `python` hung on this Windows host (likely the Store stub): factory tooling and agents should not rely on it.
- [kit] Prettier drift is still invisible to `verify.sh` (CRLF working copies make `prettier --check` fail on untouched files): add `prettier --check --end-of-line auto` to the web-lint step or drop prettier.
- [product] Bulk approve made the review queue fast but also made it easy to approve content nobody looked at; keep the per-row evidence one click away and consider a server-side "approve if unchanged since" endpoint in V2.
