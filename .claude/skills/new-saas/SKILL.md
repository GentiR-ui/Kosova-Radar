---
name: new-saas
description: Spec phase of the SaaS factory. Turns a product idea into requirements (EARS), design contracts, tasks, a scaffolded codebase and locked-ready acceptance tests, then hands over to the human for approval. Use when the user gives a new product idea in this repository.
---

# New SaaS: idea → approvable spec

Goal: a spec the human can approve in ~20 minutes. Do not ask the user questions; decide and record ADRs.
Do not start implementation in this phase.

1. **Understand the idea.** Restate it in one paragraph: user, problem, value, how we know it works in the
   market. If the idea is thin, spend a few searches on 3 comparable products (what their entry plan
   includes, pricing) to size the MVP. Prefer the smallest product that tests the hypothesis.
2. **Write `docs/requirements.md`.** Fill sections 1–2 and 6, keep platform REQ-P01..P11 unless the product
   needs a change, write product requirements REQ-001... as EARS sentences with unhappy paths and limits.
   Tag a requirement `[manual]` only if it truly cannot be automated (visual brand, email deliverability).
3. **Write `docs/design.md`.** Entities (mark tenant-owned), every endpoint with method/path/auth/payload/
   status codes, every screen with the accessible names tests will use, integrations and their status.
   Fill the background-jobs choice. Record non-obvious choices as ADRs in `docs/DECISIONS.md`.
4. **Write `docs/tasks.md`.** Keep T001/T002/T090/T091; add product slices T010+ in dependency order, each
   with REQ ids and a DoD, each small enough for one commit (< ~400 lines).
5. **Review loop.** Delegate to `spec-reviewer` with: "Review docs/requirements.md, docs/design.md,
   docs/tasks.md for <product>." Apply every BLOCKER and sensible SHOULD. Repeat until it answers
   "Spec ready for approval: yes" (max 3 rounds; then decide yourself and note open points in the summary).
   No `<FILL ...>` placeholders may remain.
6. **Scaffold.** Run `./scripts/scaffold.sh` if no `App.slnx` exists. Then `dotnet build App.slnx` and
   `cd web && npm run build` must succeed (fix scaffold issues in unlocked files).
7. **Acceptance tests.** Delegate to `test-author`: "Write the acceptance tests for every automated
   requirement in docs/requirements.md, following your instructions." Then run
   `./scripts/check-traceability.sh` — it must pass. Do not write or edit these tests yourself.
8. **Commit** `spec: draft for approval`.
9. **Hand over (in Albanian), then stop.** Send the user:
   - the product in 3 lines, the MVP scope and what is explicitly out,
   - the 3–5 most important decisions you made for them (link `docs/DECISIONS.md`),
   - counts: requirements, acceptance tests, tasks,
   - what to read before approving: `docs/requirements.md` (all), acceptance test names (skim),
   - the command to approve: `./scripts/approve-spec.sh`, and that it prints the `/goal` to start the build.
