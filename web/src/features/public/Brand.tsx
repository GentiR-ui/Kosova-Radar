/** The logo files (transparent PNG) that ship in `web/public/brand`. */

export function BrandLogo({
  tone = 'dark',
  className = 'h-8',
}: {
  /** `dark` = the variant made for dark surfaces (white "Kosova"). */
  tone?: 'dark' | 'light'
  className?: string
}) {
  return (
    <img
      src={`/brand/kosova-radar-logo-on-${tone}.png`}
      alt="Kosova Radar"
      width={1636}
      height={312}
      className={`w-auto ${className}`}
    />
  )
}

/** The "K" mark on its own: tight headers, watermarks, placeholders. */
export function BrandMark({
  className = 'h-8',
  decorative = true,
}: {
  className?: string
  decorative?: boolean
}) {
  return (
    <img
      src="/brand/kosova-radar-mark.png"
      alt={decorative ? '' : 'Kosova Radar'}
      aria-hidden={decorative || undefined}
      width={336}
      height={312}
      className={`w-auto ${className}`}
    />
  )
}
