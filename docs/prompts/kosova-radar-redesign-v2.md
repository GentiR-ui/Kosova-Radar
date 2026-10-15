# Kosova Radar — brand redesign of the public site (one pass)

Act as a senior product designer and senior frontend engineer. Redesign the **public pages** of Kosova Radar
(`web/src/features/public/**`, the public layout, `web/src/index.css`) into a modern, professional, high-contrast
product that makes people want to come back every day. Do the whole redesign in this session, without stopping to
ask about styling choices; split the work into 3 commits (see §10).

Kosova Radar answers one question at a glance: **"Çfarë ka sot në Kosovë?"** — the events of the next 3 days
(Prishtina.com, Ka' Po Dalim) and today's deals with at least 35 % off (Foleja, Gjirafa Mall), each with date,
time, place, price/discount and a link to the original source. UI language: Albanian.

## 1. Inputs (read them first)

| File | Use it for |
|---|---|
| `docs/design/reference-layout.png` | **Layout and content organisation only.** Ignore its blue colours, its English texts, the "OpenArt" watermark, its sample content (Dua Lipa, Burger House, Tera Coffee, FitLife…) and its stock photos. |
| `web/public/brand/kosova-radar-logo-on-dark.png` | Logo (transparent background, white "Kosova") for dark surfaces: header, footer, hero. |
| `web/public/brand/kosova-radar-logo-on-light.png` | Logo (transparent, dark "Kosova") for light surfaces. |
| `web/public/brand/kosova-radar-mark.png`, `kosova-radar-icon-512.png`, `favicon-64.png` | The "K" mark alone: compact header on small phones, favicon, apple-touch-icon, `theme-color`. |
| `docs/design/logo-source.png` | Colour reference only (the logo's orange gradient on charcoal). Do not ship it. |

Also read `CLAUDE.md`, `docs/design.md` §6 (screens and accessible names), ADR-044 and the current public
components, so you reuse the working hooks, routes and states.

## 2. Page structure (from the reference layout, filled with real data)

**Header (every public page)** — sticky, **solid charcoal background at every scroll position** (the menu must
never become invisible over content), `z-50`, slight shadow and height shrink after 8 px of scroll.
Left: logo (on-dark variant, height 32 px desktop / 28 px mobile, `alt="Kosova Radar"`), on screens < 380 px
the mark only. Centre: links "Ballina", "Evente", "Oferta", "Kërko" with an orange underline on the active
route. Right: the existing city selector (label "Qyteti") and a search icon button linking to `/search`.
Mobile (< 768 px): hamburger that opens a full-width sheet with the same links (keyboard and Escape work,
focus is trapped and returned).

**Home `/`**
1. **Hero (left ~2/3 on desktop)** — `h1` stays exactly "Çfarë ka sot në Kosovë". A "LIVE" badge with a pulsing
   dot when there are active events today. One short subtitle, a primary button "Shiko eventet e sotme" (selects
   the "Sot" tab and scrolls to the list), a secondary button "Ofertat e sotme". Background: the image of today's
   first event that has one, under a charcoal gradient for contrast; if there is none, a brand background
   (charcoal with a soft orange glow and a faint repeated K-mark pattern). No stock photos.
2. **Deals panel (right ~1/3)** — title "Ofertat më të mira", subtitle "Zbritje mbi 35%, vetëm sot", link
   "Shiko të gjitha" → `/deals`. The 3 active deals with the highest discount: square thumbnail, title (2 lines
   max), merchant, discount badge ("-48%"), chevron; the whole row is the link. Empty state: one line +
   link to `/deals`.
3. **Category row** — round icon chips (lucide) for the real categories: "Të gjitha", "Muzikë", "Teatër & film",
   "Art", "Sport", "Ushqim & pije", "Jeta e natës", and "Oferta" (→ `/deals`). Each links to
   `/events?category=<slug>`; the active one is filled orange. Horizontally scrollable with snap on mobile.
4. **Lower left (~7/12)** — the existing tablist **"Sot", "Evente", "Oferta"** styled like the reference's section
   header (tabs with a sliding orange indicator), then the list: thumbnail (or a category-coloured tile with the
   category icon when there is no image), date + time (or "gjatë ditës"), title, venue + city, category badge,
   countdown where it exists today, and the "Burimi: {source}" link. "Shiko të gjitha" → `/events` or `/deals`.
5. **Lower right (~5/12)** — brand promo card "Evente reale. Në kohë reale." with one sentence and a button
   "Eksploro eventet" → `/events` (charcoal, orange accent, large K-mark watermark).
6. **Footer** — charcoal, logo, short line about the sources, links to the pages.

**Other public pages** (`/events`, `/deals`, `/search`, detail pages, 404): same header, footer, cards, badges,
filters and states. Event and deal cards in a responsive grid (1 / 2 / 3–4 columns). Detail pages: large image or
brand tile, facts block (date, time, place, price), primary button "Hap burimin" plus the existing
"Burimi: {source}" link.

Remove parts of the current Lovable-based layout that do not fit this structure (e.g. the ticker); keep working
features that do (countdowns, "Nesër"/"Pasnesër" filters, city filter).

## 3. Colours (from the logo) — tokens in `index.css`, scoped to `.theme-radar`

Replace the graphite/lime public theme. Light page with charcoal header/footer/hero, like the reference.

| Token | Value | Use |
|---|---|---|
| `--color-charcoal` | `#121214` | header, footer, hero overlay, promo card |
| `--color-paper` | `#FAF8F5` | page background (warm off-white) |
| `--color-surface` | `#FFFFFF` | cards, panels |
| `--color-ink` | `#141416` | main text (17.4:1 on paper) |
| `--color-muted` | `#5C5F66` | secondary text (6.0:1 on paper) |
| `--color-line` | `#ECE7E1` | borders, dividers |
| `--color-brand` | `#FF7A00` | primary fills, active states |
| `--brand-gradient` | `linear-gradient(135deg, #FF5A00, #FFA21A)` | primary button, discount badge, active chip, LIVE badge — never whole sections |
| `--color-brand-text` | `#A84300` | orange **text** on light surfaces (5.7:1) |
| `--color-brand-on-dark` | `#FF8A1F` | orange text on charcoal (7.9:1) |
| `--color-brand-soft` / `--color-brand-ink` | `#FFE9D6` / `#7A2E00` | soft badges (8.1:1) |
| `--color-on-dark` / `--color-on-dark-muted` | `#FFFFFF` / `#B9BBC2` | text on charcoal (18.7:1 / 9.8:1) |

Hard rules: **never white text on orange** (2.6:1, fails) — text on orange is `--color-ink` (7.0:1).
Category badges use a small set of soft tints with dark text, each ≥ 4.5:1; note the ratios in a CSS comment.
Every text, icon, badge and button must stay readable on its background at every scroll position. The admin
keeps its light theme (only the font changes there).

## 4. Typography — one font everywhere

Use **Plus Jakarta Sans Variable** (`@fontsource-variable/plus-jakarta-sans`; geometric like the logo's letters)
for the whole app, public and admin, all weights from this one family; remove Manrope. Scale: h1 40–56 px /
800 / tight tracking, section titles 22–28 px / 700, card titles 16–18 px / 700, body 15–16 px / 450–500,
meta 12–13 px / 600. `tabular-nums` for prices, discounts, times and countdowns.

## 5. Components (attractive, consistent, one radius family)

- **Buttons**: primary = brand gradient, ink text, 12 px radius, 44 px min height, hover brightens + lifts 1 px,
  active scales to .97; secondary = outline on light / translucent white outline on dark; icon buttons 44×44.
- **Cards**: 16 px radius, white, 1 px line, soft layered shadow; hover lifts 4 px, deeper shadow, image zooms to
  1.04 inside a 16:10 frame; title is the stretched link, the source link stays clickable above it.
- **Badges**: "LIVE" (gradient, ink text, pulsing dot), discount "-48%" (gradient, ink text, bold, tabular),
  category (soft tint + dark text + small icon), "Oferta e sotme", "gjatë ditës".
- **Chips, tabs, inputs, selects**: pill chips; tabs with a sliding indicator; inputs 44 px with a clear orange
  focus ring.
- **States**: skeletons shaped like the cards (shimmer stops with reduced motion), friendly empty states with one
  action, error state with the existing "Provo sërish" button.
- Focus: `:focus-visible` 2 px orange ring with offset on everything interactive.

## 6. Motion (Motion for React, `motion/react`) — visible but tasteful

- Hero entrance: headline words stagger in (opacity + 12 px), subtitle and buttons follow (total < 700 ms).
- LIVE dot: soft pulse (CSS), off with reduced motion.
- Deals panel rows and category chips: staggered slide/fade in; chips scale 1.05 on hover.
- Cards: reveal on entering the viewport (`whileInView`, once, 40 ms stagger), hover lift + image zoom.
- Tabs: sliding indicator with `layoutId`; tab panel content cross-fades (`AnimatePresence`).
- Header: shadow and height transition on scroll (`useScroll`/`useTransform`), never hidden.
- Mobile menu sheet slides in; route changes fade (150 ms).
- 200–500 ms, `ease-out-expo`, opacity/transform only, no layout shift, no endless loops except the LIVE dot.
  Respect `useReducedMotion` everywhere.

## 7. Responsive

360 px first, then 640 / 768 / 1024 / 1280. Mobile order: header → hero → deals panel (horizontal snap
carousel) → categories (horizontal scroll) → tabs + list → promo card → footer. Desktop follows the reference
grid. No horizontal page scroll at 360 px on any page, 44 px touch targets, long titles wrap or clamp,
`scroll-padding-top` = header height so nothing hides under the sticky header.

## 8. Do not break

- Keep these exact accessible names (locked acceptance tests): `h1` "Çfarë ka sot në Kosovë", "Evente",
  "Oferta", "Kërkim"; tablist with tabs "Sot", "Evente", "Oferta"; searchbox "Kërko" and button "Kërko";
  select "Qyteti"; links "Burimi: {source}"; texts "Verifikuar së fundmi", "Provo sërish", "Nuk u gjet asnjë
  rezultat", "Ky event ka përfunduar", "Kjo ofertë ka skaduar", "Vlen deri më …", "Të dhëna testuese",
  discount text "-{n}%"; card titles as headings/links; `article` cards. Check `docs/design.md` §6 for the rest.
- Real data only. Never show the reference image's sample items, invented deals, ratings or photos.
- Do not change the API, ingestion, routes or admin behaviour.

## 9. Assets and metadata

Header/footer use the transparent logo files above. Set favicon (`favicon-64.png`), apple-touch-icon
(`kosova-radar-icon-512.png`), `theme-color` `#121214`, and use the logo for the default social preview image if
one exists. Optimise images (width/height attributes, `loading="lazy"` below the fold).

## 10. Work order and verification

1. Commit 1 — tokens, font, logo assets, header/footer, shared components (buttons, cards, badges, chips, states).
2. Commit 2 — home page in the reference structure with motion.
3. Commit 3 — events, deals, search, detail and 404 pages; responsive polish.

After each commit: `./scripts/verify.sh --quick` and `cd web && npm run build`. At the end (Docker running,
`scripts/dev.sh` stopped): `cd web && npm run e2e:acceptance` for the public UI tests. Take screenshots of every
public page at 360, 768 and 1280 px into `web/test-results/redesign/` and look at them; check keyboard-only
navigation, contrast of every new colour pair, reduced motion and the browser console. Stop every server you
started. Record an ADR "Public UI v3: brand from the logo, layout from the reference" (supersedes the look of
ADR-044), update the Visual direction in `docs/design.md` §6, and add a PROGRESS entry. Do not claim a check
passed unless you ran it.

End with a short summary in Albanian: what changed, the screenshots to look at, checks run, anything left.
