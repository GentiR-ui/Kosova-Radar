import { cn } from '../../lib/cn'
import { p } from './strings'

/**
 * The logo lockup: the K and the wordmark are the two halves of the original artwork in
 * `web/public/brand` (background removed, trimmed to the ink), so the colours are the client's own.
 * The wordmark is sized to about half the K's height, the proportion it has in that artwork.
 */
export function BrandLogo({
  tone = 'light',
  className,
  tagline = false,
}: {
  /** `light` = the variant made for light surfaces; `dark` = for dark surfaces. */
  tone?: 'light' | 'dark'
  className?: string
  /** Show the brand line directly under the wordmark. */
  tagline?: boolean
}) {
  return (
    <span className={cn('flex items-center gap-2.5', className)}>
      <span aria-hidden="true" className="brand-k h-9 shrink-0 md:h-10" />
      <span className="flex flex-col justify-center">
        <span
          role="img"
          aria-label="Kosova Radar"
          className={cn(
            // self-start: the width must come from the artwork's ratio, not from the tagline below.
            'brand-word h-[17px] self-start md:h-[19px]',
            tone === 'dark' && 'brand-word-dark',
          )}
        />
        {tagline && (
          <span
            className={cn(
              'mt-[3px] whitespace-nowrap text-[10px] font-semibold leading-none max-sm:hidden md:text-[11px]',
              tone === 'dark' ? 'text-on-dark-muted' : 'text-muted',
            )}
          >
            {p.tagline}
          </span>
        )}
      </span>
    </span>
  )
}

/** The "K" mark on its own: tight headers, watermarks, placeholders. */
export function BrandMark({ className = 'h-9' }: { className?: string }) {
  return <span aria-hidden="true" className={cn('brand-k block', className)} />
}
