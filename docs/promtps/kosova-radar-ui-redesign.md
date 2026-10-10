# Kosova Radar — Professional UI/UX Redesign & Frontend Polish

Act as a senior product designer and senior frontend engineer. Your task is to transform the existing **Kosova Radar** application into a polished, modern, premium-quality product that feels like a real, production-ready platform for discovering what's happening and what is worth checking out in Kosovo.

This is an existing project, not a new application. Work directly within the current repository.

## 1. Understand the Product First

Kosova Radar brings together:

* Events and activities happening in Kosovo.
* Deals, special offers, and price drops.
* Personalized discovery and search.
* Relevant alerts and useful updates.
* Clear source attribution and trustworthy information.

The core product idea is: **"Çka ia vlen tani?" — What is worth checking out right now?**

The UI should communicate discovery, relevance, freshness, trust, and local usefulness. It should feel like a modern consumer product, not a generic admin dashboard, a basic directory, or a template-generated landing page.

Before making changes:

1. Inspect `CLAUDE.md`, the project structure, existing routes, components, styles, package.json, and the current application.
2. Identify which pages and components users actually interact with.
3. Understand the existing design, data flow, authentication, API integrations, and component conventions.
4. Identify the most important visual problems and usability issues.
5. If the current implementation differs from the expected stack, follow the actual repository rather than blindly introducing a new stack.

Then implement the redesign directly. Do not stop after producing a design plan.

## 2. Establish a Consistent Design System

Create a cohesive design language used throughout the entire application.

### Typography

* Use one modern, professional font family consistently across the application.
* Prefer Geist or Inter, selecting whichever integrates most naturally with the current project.
* Establish a clear type scale for headings, descriptions, navigation, labels, metadata, and body text.
* Use appropriate font weights, line heights, and letter spacing.
* Eliminate inconsistent fonts, arbitrary text sizes, and visually disconnected components.

### Colors and Contrast

* Establish a deliberate color palette with a clear primary color, neutral surfaces, strong text colors, secondary text, borders, and semantic status colors.
* The visual identity should suit a modern Kosovo-focused discovery and radar platform.
* Explore a restrained dark-navy or charcoal foundation with a distinctive radar-inspired accent, such as electric blue or teal. Use the existing brand identity if one is already established.
* Keep the interface bright, readable, and visually balanced where appropriate. Do not make every section dark or cover the page in gradients.
* Ensure text, icons, badges, buttons, navigation, and card backgrounds remain readable against their surrounding surfaces.
* Meet WCAG AA contrast targets wherever applicable.
* Fix any navigation or menu elements that become invisible or difficult to read while scrolling.
* Define reusable design tokens instead of scattering arbitrary colors throughout the codebase.

### Components

Create a consistent visual style for:

* Primary and secondary buttons.
* Search inputs and filters.
* Event and deal cards.
* Category chips and badges.
* Navigation links and dropdowns.
* Labels, status indicators, and metadata.
* Empty states, loading states, and error messages.
* Modals, notifications, pagination, and other existing interactive components.

Use consistent border radii, spacing, shadows, hover states, focus states, and transitions. Components should look intentional and professionally designed rather than like unrelated UI elements.

## 3. Redesign the Main Discovery Experience

Give the existing public-facing pages a strong visual hierarchy and make content easy to discover.

### Navigation

* Create a clean, professional, responsive navigation system.
* Ensure the logo and brand identity are clear.
* Make navigation readable on every background and at every scroll position.
* If a sticky header exists or is appropriate, implement it properly, including a subtle background or backdrop blur when scrolling.
* Prevent overlapping elements, invisible links, and incorrect z-index behavior.
* Provide a polished mobile navigation menu with working interactions.

### Main Feed and Content Cards

* Make events and deals visually attractive, scannable, and easy to compare.
* Improve card layouts, image treatment, spacing, typography, and information hierarchy.
* Display important information prominently, such as titles, categories, locations, dates, prices, discounts, and source attribution, whenever that information is available in the existing data.
* Use clear visual indicators for genuinely relevant states, such as new items, upcoming events, or price reductions, only when supported by the data.
* Make the primary action on each card obvious.
* Use consistent card heights and responsive grid layouts where appropriate.
* Add subtle hover interactions, carefully controlled shadows, and polished transitions.
* Avoid excessive borders, oversized empty spaces, repetitive layouts, and unnecessary decorative elements.

### Search and Filtering

* Improve the visual prominence and usability of search.
* Make existing category filters, sorting controls, and other discovery tools intuitive.
* Ensure active filters are visually distinguishable.
* Preserve their actual functionality. Do not create decorative controls that do nothing.

### Brand Identity

* Use subtle radar-inspired visual details where they reinforce the product identity.
* Use real existing content and assets where available.
* Avoid excessive radar circles, glowing effects, gradients, generic AI illustrations, and distracting backgrounds.
* Prioritize the content and the usefulness of the product.

## 4. Motion Design and Scroll Animations

Use **Motion for React** as the primary animation library.

Official documentation: https://motion.dev/docs/react-scroll-animations

Import from `motion/react` and use the relevant APIs, including `useScroll`, `useTransform`, `whileInView`, `AnimatePresence`, and `useReducedMotion` where appropriate.

Implement a deliberate motion system:

* **Page entrance:** subtle, polished entrance animations for key page sections.
* **Scroll reveals:** cards and content sections gently fade in and move into position as they enter the viewport.
* **Staggered content:** introduce small stagger delays for groups of cards when it improves the experience.
* **Scroll-linked effects:** use `useScroll` and `useTransform` selectively for subtle progress indicators, header transitions, or small parallax effects where they genuinely improve the design.
* **Micro-interactions:** add refined hover, tap, focus, and state-change animations to buttons, cards, filters, and navigation.
* **Transitions:** animate existing dialogs, dropdowns, tabs, and expandable elements smoothly.
* **Loading states:** polish loading feedback without creating distracting or unnecessarily long animations.

Animation requirements:

* Motion must enhance usability, not compete with the content.
* Avoid animating every element independently.
* Avoid excessive bounce, spinning, dramatic parallax, and continuous decorative animations.
* Prefer performant properties such as opacity and transform.
* Do not introduce noticeable scroll lag or layout shifts.
* Respect `prefers-reduced-motion` and provide accessible alternatives.
* Avoid adding multiple animation libraries that duplicate Motion's capabilities.

Use scroll-linked animations only when appropriate. Most content reveals should use viewport-triggered animations rather than expensive continuous scroll handlers.

## 5. Responsive Design

The entire experience must work professionally on:

* Small mobile phones.
* Larger mobile devices.
* Tablets.
* Laptops.
* Large desktop screens.

Do not simply shrink the desktop layout.

Specifically:

* Eliminate horizontal overflow.
* Make navigation usable on small screens.
* Adapt card grids and content spacing to available width.
* Ensure buttons and touch targets are comfortable to use.
* Keep typography readable without awkward wrapping.
* Make search, filters, and dialogs work on mobile.
* Prevent sticky headers and floating elements from covering content.
* Check images, long titles, metadata, and empty states at narrow widths.

## 6. Recommended Libraries

Inspect the dependencies already installed before adding anything. Prefer the following tools when they fit the existing stack:

1. **Motion for React (`motion`)** — primary animation library and scroll effects.
   https://motion.dev/

2. **Tailwind CSS** — consistent styling and responsive design, if already used by the project.
   https://tailwindcss.com/

3. **shadcn/ui** — accessible, customizable UI building blocks for dialogs, dropdowns, buttons, inputs, and other components, if compatible with the current React/Next.js stack.
   https://ui.shadcn.com/

4. **Lucide React (`lucide-react`)** — consistent, lightweight interface icons.
   https://lucide.dev/guide/react/

5. **Sonner** — polished toast notifications, only if the application needs them and does not already have an appropriate notification system.
   https://sonner.emilkowal.com/

6. **next/font** — optimized font loading if the application uses Next.js.
   https://nextjs.org/docs/app/getting-started/fonts

Do not install every library automatically. Reuse the existing stack, inspect compatibility, and introduce only dependencies that provide clear value. Do not replace working components or upgrade major dependencies solely for visual reasons.

Use shadcn/ui as a foundation where appropriate, but customize the components to create a distinctive Kosova Radar identity rather than leaving the default template appearance.

## 7. Preserve Functionality and Data Integrity

This is a visual and user-experience improvement, not permission to rebuild the application architecture.

* Preserve existing routes, business logic, API calls, authentication, database interactions, and existing features.
* Preserve the current Supabase integration and data flow if present.
* Do not replace real data with hardcoded mock content.
* Do not invent events, deals, discounts, prices, ratings, or claims.
* Do not break source attribution or links to original event and deal sources.
* Do not modify the ingestion pipeline, database schema, security policies, or backend behavior unless a change is genuinely required and explicitly justified.
* Maintain existing authorization rules and administrative functionality.
* Keep server and client component boundaries correct if using Next.js.
* Avoid hydration errors, unnecessary client components, duplicated code, and oversized dependencies.
* Keep existing working features working.

## 8. Implementation and Quality Assurance

Work systematically but efficiently.

1. Inspect the current application and identify the highest-impact UI issues.
2. Establish reusable design tokens and shared component styles.
3. Improve the global typography, colors, navigation, and layout.
4. Redesign the primary discovery experience and reusable content cards.
5. Improve remaining existing pages and interactive components for consistency.
6. Add purposeful Motion animations and responsive behavior.
7. Run the existing lint, type-check, build, and relevant tests using the commands supported by the repository.
8. Fix regressions introduced by your changes.
9. Verify the main interactions and check the UI at mobile, tablet, and desktop widths. Use the existing browser-testing tools if available.
10. Check contrast, keyboard navigation, reduced-motion behavior, loading states, and console errors.

Do not claim that a test passed unless you actually ran it. If a verification step cannot run because of an environmental limitation, clearly report that limitation.

## Definition of Done

The task is complete when:

* The application has a coherent, modern, professional visual identity.
* Typography, colors, spacing, buttons, labels, cards, and navigation are consistent.
* The navigation remains visible and readable while scrolling.
* Scroll animations and micro-interactions are polished, restrained, and performant.
* The main discovery experience is attractive and easy to use.
* Existing pages are responsive across mobile, tablet, and desktop.
* Existing functionality and data integrations remain intact.
* Relevant checks have been executed and any introduced regressions have been fixed.
* The implementation uses a reasonable number of dependencies and avoids unnecessary architectural work.

Start by inspecting the repository, then implement the improvements. Make sensible design decisions independently instead of stopping to ask about minor styling choices.

At the end, provide a concise summary of the main improvements, the files and components changed, the libraries added (if any), the checks performed, and any remaining issues.

The final result should feel like a real, trustworthy, modern Kosovo discovery platform — not merely the same website with different colors.
