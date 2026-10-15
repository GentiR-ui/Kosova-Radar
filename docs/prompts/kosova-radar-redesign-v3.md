# Kosova Radar — premium redesign v4 (task T059, one pass)

Act as a senior product designer and senior frontend engineer. Make the **public site** of Kosova Radar feel
premium, warm and inviting — a place people enjoy browsing — without changing how the content is organised beyond
what is described here. Work in this session until done; do not stop to ask about styling choices. Split the work
into 3 commits (§10). Frontend only: no API, ingestion or admin behaviour changes.

## 1. Inputs — read before writing code

| Source | Use it for |
|---|---|
| `docs/design/reference-v3.png` | **The target look**: colours, header, hero, deals panel, icon tiles, chips, section header "Sot në Kosovë", large cards. Its photos and product names are sample content — never ship them. |
| `C:\TiaraHomeFurniture` (the client's furniture shop, newest uncommitted design included) | **How products are shown on its category pages**: grid, card proportions, image treatment, hover and entrance animations, filter bar, spacing and colour contrast. Read the category/product-list page and its card components (and their CSS/animation code) and reproduce that *quality and feel* for event and deal cards. Do not copy its brand, texts or images, and do not modify that folder. |
| `web/public/brand/kosova-radar-logo-violet-on-light.png`, `…-violet-on-dark.png`, `kosova-radar-mark-violet.png`, `kosova-radar-icon-violet-512.png`, `favicon-violet-64.png` | The logo recoloured to this palette (transparent). Header uses on-light; footer/dark surfaces use on-dark; favicon/apple-touch from the icon. |
| `CLAUDE.md`, `docs/design.md` §6, ADR-048, ADR-049, the current `web/src/features/public/**` and `web/src/index.css` | Existing routes, hooks, states and the accessible names tests rely on. Reuse the working data hooks. |

## 2. Libraries — the best tool for each job (check `package.json`, add only these if missing)

| Need | Library |
|---|---|
| Animations, hover, reveals, layout transitions | **Motion for React** (`motion`, already installed) |
| Icons | **lucide-react** (already installed), inside coloured tiles (§4) |
| Font | **Plus Jakarta Sans Variable** (`@fontsource-variable/plus-jakarta-sans`, already installed) — the only font, public and admin |
| Swipeable rows (deals panel and category chips on mobile, optional "more today" rail) | **Embla Carousel** (`embla-carousel-react`) |
| Mobile menu sheet, popovers | **Radix UI** primitives (`@radix-ui/react-dialog`, `@radix-ui/react-popover`) — accessible focus handling |
| Class composition | `clsx` + `tailwind-merge` |

Keep native `<select>` elements for filters (tests drive them with `selectOption`); style them (custom chevron,
pill shape). No other UI kit, no second animation or icon library.

## 3. Colours — from the reference (tokens in `index.css`, public theme class)

| Token | Value | Use |
|---|---|---|
| `--color-paper` | `#F3F6FA` | page background |
| `--color-surface` | `#FFFFFF` | header, cards, panels |
| `--color-ink` | `#0B1B3F` | titles and body (15.6:1 on paper) |
| `--color-muted` | `#5B6478` | secondary text (5.5:1 on paper) |
| `--color-line` | `#E6EAF2` | borders |
| `--color-violet` | `#6D4AF6` | brand, active nav underline, links (5.3:1 on white) |
| `--color-blue` | `#2F5BEA` | gradient end |
| `--brand-gradient` | `linear-gradient(135deg, #5B3FE8, #2F5BEA)` | primary buttons, discount badges, active chip — **white text** (≥ 5.5:1 at both ends) |
| `--headline-gradient` | `linear-gradient(90deg, #C044FA, #A78BFA)` | the word "Kosovë?" in the hero only (on the dark hero) |
| `--color-hero` | `#03142E` | hero overlay base (white text 18.4:1) |
| `--color-violet-soft` | `#EDE9FE` | hover tints, active chip ring |

Icon tiles (soft background + strong icon, all ≥ 4.5:1): orange `#FFF1E6`/`#C2410C`, cyan `#E0F7FC`/`#0E7490`,
pink `#FCE7F3`/`#BE185D`, indigo `#E0E7FF`/`#4338CA`, violet `#EDE9FE`/`#6D28D9`, green `#DCFCE7`/`#15803D`,
red `#FEE2E2`/`#B91C1C`, amber `#FEF3C7`/`#B45309`. One fixed colour per category, reused everywhere that
category appears (chip, card badge, placeholder tile). Never light blue (`#609AFD`) or lighter with white text.
Write the contrast ratios in a CSS comment. The admin keeps its own light theme.

## 4. Header (modern, like the reference)

White, sticky, `backdrop-blur` with 90 % white when scrolled plus a soft shadow; always readable. Left: logo
(on-light, ~36 px high) with the tagline "Zbulo më të mirën në Kosovë" in small muted text below the wordmark
(hidden < 640 px). Centre: links "Ballina", "Eventet", "Ofertat", "Kërko" — medium weight, ink colour, active link
violet with a 2 px rounded underline that slides between links (`layoutId`). Right: a pill city selector with a
map-pin icon (native select, **label "Zgjidh qytetin"**, so it never collides with the "Qyteti" filter on
/events) and a round 44 px search button (soft blue-grey background, magnifier) linking to `/search`.
< 768 px: logo + search + hamburger; the menu opens as a Radix Dialog sheet from the right with the links, the
city selector, focus trap and Escape.

## 5. Home — keep the current organisation, with these sections in this order

1. **Hero** (left, ~2/3) — keep as is in content; restyle like the reference: rounded 28 px dark panel, photo of
   today's first event with an image (else a brand gradient with a faint K-mark pattern) under a
   `--color-hero` gradient, "LIVE" pill with a green pulsing dot, `h1` exactly "Çfarë ka sot në Kosovë" with
   "Kosovë?" in `--headline-gradient`, the subtitle, primary button "Shiko eventet e sotme" (gradient, calendar
   icon, arrow that slides on hover) → scrolls to "Sot në Kosovë", secondary glass button "Ofertat e sotme"
   → `/deals`.
2. **Deals panel** (right, ~1/3) — stays where it is: a `section` labelled by its `h2` **"Ofertat më të mira"**
   (tag icon), subtitle "Zbritje mbi 35%, vetëm sot", link "Shiko të gjitha →". Three rows like the reference:
   square product image on a light tile, title as a heading (2 lines max), merchant, gradient discount badge
   "-42%", chevron; whole row clickable; rows lift and the chevron moves on hover. On mobile: Embla carousel.
3. **Category chips** — pill chips with the coloured icon tiles from §3 (lucide icons: Star "Të gjitha",
   Palette "Art & ekspozita", GraduationCap "Konferenca & edukim", Users "Familje & fëmijë", Music "Muzikë &
   koncerte", Wine "Jeta e natës", Trophy "Sport", Clapperboard "Teatër & film", Utensils "Ushqim & pije",
   Tag "Oferta"); the active chip is filled with the brand gradient and white text. Wrap on desktop, swipe on
   mobile. Each links to `/events?category=<slug>` ("Oferta" → `/deals`).
4. **"Sot në Kosovë"** — replaces the old "Sot / Evente / Oferta" tabs. A `section` labelled by its `h2`
   **"Sot në Kosovë"** (calendar icon) with "Shiko të gjitha →" to `/events`. Only **today's active events**,
   as **large cards** in a 3-column grid (2 on tablet, 1 on phone): image 16:10 with rounded 20 px corners, a
   small "Event" or category badge on the image, time chip ("20:00" or "gjatë ditës"), title as a heading,
   venue + city with a map-pin icon, countdown when it starts within hours, "Burimi: {source}" link. Empty
   state when nothing is on today: friendly illustration-free card + link "Shiko eventet e nesërme" →
   `/events`. The home `?search=` filters both this section and the deals panel.
5. **Footer** — dark (`--color-hero`), logo on-dark, short line about the sources, page links.

Remove the old tablist and anything else on the home page not listed here.

## 6. /events and /deals — large, premium cards like the Tiara category pages

- Page header: `h1` stays "Evente" / "Oferta", a short subtitle, result count, and a sticky filter bar
  (styled native selects "Qyteti" and "Kategoria", date inputs on /events, search input) that shows active
  filters as removable chips.
- Grid of **large cards** (3 → 2 → 1 columns; 4 on ≥ 1536 px) with the Tiara-style proportions, generous
  padding and image treatment. Event card: image or category-coloured placeholder tile with the icon, date
  block (day + month), time, title, venue + city, category badge, source link. Deal card: product image on a
  soft tile, merchant, title, old price struck through, new price large, gradient "-48%" badge, "Oferta e
  sotme" badge for listing deals, source link.
- Detail pages and `/search` use the same cards, badges and facts styling.

## 7. Motion — premium but calm (Motion for React)

- Cards: on hover lift 6 px, deeper layered shadow, image zoom 1.06, a soft light sweep across the image,
  badge and arrow nudge; on tap scale .98. Entrance: fade + 16 px rise when entering the viewport, staggered
  50 ms per card, once.
- Hero: headline words stagger in, buttons follow; LIVE dot pulse.
- Deals rows and chips: staggered slide-in; chips scale 1.04 and tint on hover.
- Header: underline slides between links; shadow/blur appear on scroll.
- Filters: chips added/removed with `AnimatePresence`; grid re-orders with `layout` animation.
- 200–500 ms, ease-out-expo, opacity/transform only, no layout shift. Everything respects
  `useReducedMotion` / `prefers-reduced-motion` (no movement, plain fades).

## 8. Must not break

- Keep exactly: `h1` "Çfarë ka sot në Kosovë", "Evente", "Oferta", "Kërkim"; regions "Sot në Kosovë" and
  "Ofertat më të mira" (each a `section` with `aria-labelledby` pointing at its `h2`; item titles are headings);
  filter label "Qyteti" on /events and /deals; searchbox and button "Kërko" on /search; links
  "Burimi: {source}"; texts "Verifikuar së fundmi", "Kontrolluar së fundi më …", "Vlen deri më …",
  "Nuk u gjet asnjë rezultat", "Diçka shkoi keq." + button "Provo sërish", "Ky event ka përfunduar",
  "Kjo ofertë ka skaduar", "Të dhëna testuese", "-{n}%", old price in `<s>`; result labels "Event"/"Ofertë".
  Check `docs/design.md` §6 and `web/e2e/acceptance/*.spec.ts` (read-only) for anything else.
- Real data only — no sample products, photos or prices from the reference or from Tiara.
- 44 px touch targets, visible `:focus-visible` ring (violet, 2 px, offset), AA contrast for every pair,
  no horizontal scroll at 360 px, `scroll-padding-top` = header height.

## 9. Verify

After each commit: `./scripts/verify.sh --quick` and `cd web && npm run build`. At the end, with Docker running
and `scripts/dev.sh` stopped: `cd web && npm run e2e:acceptance` (all UI acceptance tests, desktop + mobile) and
`npm run e2e`. Screenshot `/`, `/events`, `/deals`, `/search`, one event and one deal detail at 360, 768 and
1280 px into `web/test-results/redesign-v4/` and look at them; check keyboard-only use, contrast, reduced motion
and the browser console. Stop every server you started. Do not claim a check passed unless you ran it.

## 10. Commits and records

1. Tokens, font, logo, header, footer, shared components (buttons, chips, icon tiles, badges, cards, states).
2. Home page (§5) with motion.
3. /events, /deals, /search and detail pages (§6), responsive polish.

Then check T059 in `docs/tasks.md`, add a PROGRESS entry and an ADR "Public UI v4" (palette, libraries added,
what was taken from the Tiara pages), and end with a short summary in Albanian: what changed, which screenshots to
open, checks run, anything left.
