import {
  Clapperboard,
  GraduationCap,
  Music,
  Palette,
  Sparkles,
  Tag,
  Trophy,
  Users,
  UtensilsCrossed,
  Wine,
  type LucideIcon,
} from 'lucide-react'

/**
 * One fixed tile colour and icon per category slug (ADR-050). The same pair is used on the chip,
 * the card badge and the placeholder tile, so a category always looks the same.
 * The tile classes and their contrast ratios live in `index.css`.
 */
const categories: Record<string, { tile: string; Icon: LucideIcon }> = {
  // events
  art: { tile: 'tile-pink', Icon: Palette },
  edukim: { tile: 'tile-cyan', Icon: GraduationCap },
  familje: { tile: 'tile-violet', Icon: Users },
  muzike: { tile: 'tile-indigo', Icon: Music },
  nate: { tile: 'tile-amber', Icon: Wine },
  sport: { tile: 'tile-green', Icon: Trophy },
  teater: { tile: 'tile-red', Icon: Clapperboard },
  ushqim: { tile: 'tile-orange', Icon: UtensilsCrossed },
  // deals
  mode: { tile: 'tile-pink', Icon: Tag },
  sherbime: { tile: 'tile-green', Icon: Sparkles },
  shtepi: { tile: 'tile-amber', Icon: Tag },
  teknologji: { tile: 'tile-indigo', Icon: Tag },
  udhetime: { tile: 'tile-cyan', Icon: Tag },
}

export const allCategoriesStyle = { tile: 'tile-violet', Icon: Sparkles }
export const dealsStyle = { tile: 'tile-orange', Icon: Tag }

export function categoryStyle(slug: string | undefined, kind: 'event' | 'deal' = 'event') {
  return (
    (slug ? categories[slug] : undefined) ?? (kind === 'deal' ? dealsStyle : allCategoriesStyle)
  )
}
