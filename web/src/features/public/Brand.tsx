/** The logo files (transparent PNG) that ship in `web/public/brand`, recoloured to the v4 palette. */

export function BrandLogo({
  tone = 'light',
  className = 'h-9',
}: {
  /** `light` = the variant made for light surfaces (dark "Kosova"); `dark` = for dark surfaces. */
  tone?: 'light' | 'dark'
  className?: string
}) {
  return (
    <img
      src={`/brand/kosova-radar-logo-violet-on-${tone}.png`}
      alt="Kosova Radar"
      width={1636}
      height={312}
      className={`w-auto ${className}`}
    />
  )
}

/** The "K" mark on its own: tight headers, watermarks, placeholders. */
export function BrandMark({
  className = 'h-9',
  decorative = true,
}: {
  className?: string
  decorative?: boolean
}) {
  return (
    <img
      src="/brand/kosova-radar-mark-violet.png"
      alt={decorative ? '' : 'Kosova Radar'}
      aria-hidden={decorative || undefined}
      width={336}
      height={312}
      className={`w-auto ${className}`}
    />
  )
}
