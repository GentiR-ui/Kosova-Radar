# SaaS Starter — fabrika personale për SaaS me Claude Code

Ky repo nuk është aplikacion. Është **pipeline-i** me të cilin ndërtohet çdo SaaS i ri: rregullat, rolet e
agjentëve, kontrollet automatike dhe stack-u default. Për çdo produkt e klonon, i jep Claude idenë,
aprovon specifikimin një herë, dhe Claude e ndërton deri te release candidate pa të pyetur.

```
IDEA ─► requirements.md (EARS) ─► design.md ─► tasks.md ─► spec-reviewer
     ─► scaffold ─► test-author shkruan acceptance tests
     ─► TI APROVON  (./scripts/approve-spec.sh → kyçen kërkesat + acceptance tests)
     ─► /goal BUILD LOOP:  task → kod → teste → verify --quick → reviewer (+ security) → fix → commit → next
     ─► RELEASE: hardening · review i plotë · QA në browser · verify i plotë · RELEASE.md · LESSONS.md
     ─► TI BËN DEPLOY ─► përdorues realë ─► feedback → --amend → loop i ri
```

## Parimi

Claude nuk "bën gjithçka vetë". Rolet janë të ndara dhe verifikimi nuk varet nga agjenti që shkruan kodin:

| Rol | Kush | Çfarë nuk mund të bëjë |
|---|---|---|
| Implementuesi | sesioni kryesor i Claude | të ndryshojë kërkesat ose acceptance tests |
| `test-author` | subagjent | të lexojë kodin (`src/`, `web/src/`) — testet dalin nga specifikimi |
| `reviewer`, `security-reviewer`, `spec-reviewer`, `qa` | subagjentë | të editojnë kodin — vetëm raportojnë |
| Gate-i | `scripts/verify.sh` + hooks | — është deterministik, jo opinion i modelit |
| Përfundimi | `/goal` (vlerësues i veçantë) | — nuk e vendos agjenti që punon |

**Çfarë kyçet pas aprovimit:** `docs/requirements.md`, `tests/App.AcceptanceTests/`, `web/e2e/acceptance/`.
**Çfarë mbetet e lirë:** unit, integration dhe regression tests dhe `web/e2e/flows`.
**Çfarë është vetëm e jotja:** `scripts/`, `.claude/`, `.factory/` dhe `Directory.Build.*` (hooks bllokojnë ndryshimet).
Kështu Claude mund t'i ndryshojë testet teknike kur ndryshon implementimi, por nuk mund ta ulë standardin
e "done".

## Kërkesat

Claude Code (version me `/goal` dhe auto mode), .NET 10 SDK, Node.js 22+, Docker (për PostgreSQL,
Testcontainers dhe E2E), git, `jq` dhe `perl`. Punon në macOS (bash 3.2) dhe Linux; në Windows përdor WSL.
Pa `jq`, hooks i bllokojnë të gjitha veprimet derisa ta instalosh (`brew install jq`).

## Përdorimi për një SaaS të ri

```bash
# 1. Krijo projektin nga kit-i
./scripts/new-project.sh ../emri-produktit
cd ../emri-produktit

# 2. Nise Claude Code në auto mode dhe shkruaj idenë (sa më shumë detaje, aq më mirë)
claude --permission-mode auto
> Ndërto një SaaS për ... (për kë, çfarë problemi, si fiton para)
```

Claude ekzekuton skill-in `new-saas` dhe ndalet kur specifikimi është gati, me një përmbledhje në shqip.

```bash
# 3. ~20 minuta: lexo docs/requirements.md dhe emrat e acceptance tests, pastaj:
./scripts/approve-spec.sh
```

Skripti i kyç skedarët, krijon tag-un `spec-v1` dhe të printon komandën `/goal`. E ngjit në Claude Code dhe e
lë të punojë. Mund ta mbyllësh terminalin: `claude --continue` e rikthen goal-in.

```bash
# 4. Kur mbaron: lexo docs/RELEASE.md, kontrollo checklist-ën dhe bëj deploy vetë.
```

Nëse Claude përfundon me **NEEDS HUMAN**, ka mbaruar gjithçka që s'varet nga ti dhe të jep listën e saktë
(p.sh. çelësat e Stripe, një acceptance test i kontestuar). Për ta lënë një kërkesë jashtë gate-it
derisa ta zgjidhësh, shto `REQ-004 | arsyeja` në `.factory/waivers` dhe vazhdo loop-in.

Për të ndryshuar kërkesat më vonë: i ndryshon vetë (ose i kërkon Claude një draft në `docs/BLOCKERS.md`),
pastaj `./scripts/approve-spec.sh --amend "arsyeja"`.

## Dy pikat ku je ti, dhe pse

1. **Aprovimi i specifikimit.** Aty merren vendimet që AI nuk duhet t'i marrë për ty: çfarë ndërtohet dhe si
   matet "done".
2. **Deploy-i.** Sekretet, domain-i, pagesat dhe të dhënat e përdoruesve të vërtetë janë të pakthyeshme.

Nëse do autonomi të plotë edhe në pikën 1: vendos `SELF_APPROVE=1` në `.factory/factory.conf` dhe hiq
rreshtat `approve-spec` nga `deny` në `.claude/settings.json`. Nuk e rekomandoj për produkte reale.

## Struktura

```
CLAUDE.md                     rregullat (të shkurtra, të shkruara me dorë)
docs/requirements.md          ÇFARË — EARS + REQ ids (platforma REQ-P01..P11 e gatshme)
docs/design.md                SI — stack, tenancy, entitete, kontratat e API-së dhe UI-së
docs/tasks.md                 RENDI — vertical slices me REQ ids dhe DoD
docs/DECISIONS.md             ADR për çdo vendim që Claude mori për ty
docs/PROGRESS.md · BLOCKERS.md · LESSONS.md · RELEASE.md
docs/stack/dotnet-react.md    konventat e kodit për stack-un default
.claude/agents/               spec-reviewer, test-author, reviewer, security-reviewer, qa
.claude/skills/               new-saas, build-loop, harden, release
.claude/hooks/                protect-spec, secret-guard, format, stop-gate
.claude/settings.json         lejet (allow/deny) + hooks
.factory/factory.conf         çfarë kyçet, autonomia, kufijtë
scripts/                      scaffold, approve-spec, verify, check-traceability, restore-spec, new-project
Dockerfile · docker-compose.yml · .github/workflows/ci.yml
```

## Stack-u default

React + TypeScript (Vite, TanStack Query, Tailwind) · ASP.NET Core 10 minimal APIs · Clean/Onion me 4
projekte · EF Core + PostgreSQL · Identity me cookie · multi-tenancy me `TenantId` + query filter global ·
xUnit + Testcontainers + Playwright · Docker + GitHub Actions. Redis dhe Hangfire shtohen vetëm kur një
kërkesë i justifikon (me ADR). Emrat e projekteve janë fiks (`App.*`), kështu Dockerfile, CI dhe verify nuk
ndryshojnë nga një produkt te tjetri.

## Si bëhet kit-i më i mirë me çdo projekt

Në fund të çdo projekti, `docs/LESSONS.md` ka pikat e shënuara `[kit]`. Kopjoji këtu: kërkesa të reja
platforme, rregulla, fixe për hooks/scripts. Pas projektit të parë, kopjo edhe kodin e platformës (T001/T002:
auth, tenancy, health, layout-in e web-it) në `scripts/scaffold.sh` ose si skedarë të gatshëm. Kështu
projekti i dytë fillon me bazën e testuar dhe Claude merret vetëm me produktin.

## Kufizime

- Hooks janë mbrojtje kundër gabimeve dhe shkurtesave, jo kufi sigurie. Prandaj `verify.sh` kontrollon
  skedarët e kyçur me checksum, numëron sa acceptance tests kaluan kundrejt sa janë deklaruar (kap testet e
  anashkaluara ose të përjashtuara), dhe `/goal` e kërkon atë raport në transkript.
- Kodi i aplikacionit mund teorikisht të sillet ndryshe gjatë testeve. Këtë e kap `reviewer` (e trajton si
  HIGH), jo një kontroll automatik.
- Kodi C# që krijon `scaffold.sh` (p.sh. `AppFactory`) nuk u kompilua dot gjatë ndërtimit të kit-it, sepse
  NuGet nuk ishte i arritshëm aty. Hooks, skriptet, logjika e kyçjes dhe pjesa frontend u testuan. Nëse
  diçka nuk kompilohet, kapet në fazën e specifikimit (para kyçjes), kur `test-author` mund ta rregullojë.
- Hooks u testuan në Linux. Për macOS u shmangën veglat vetëm-GNU (`realpath -m`, `sed -i`, `xargs -d`,
  `sha256sum`), por nuk u ekzekutuan në një Mac real.
- Pa Docker, `verify.sh` i plotë dështon qëllimisht (integration, acceptance API dhe E2E kërkojnë PostgreSQL).
