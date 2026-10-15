import {
  CalendarDays,
  Film,
  GraduationCap,
  Music,
  Palette,
  Tag,
  Trophy,
  Users,
  UtensilsCrossed,
  Wine,
  type LucideIcon,
} from 'lucide-react'

/** Colour tint and icon per real category slug (the `.cat-*` classes carry the contrast ratios). */
const categories: Record<string, { tint: string; Icon: LucideIcon }> = {
  muzike: { tint: 'cat-muzike', Icon: Music },
  teater: { tint: 'cat-teater', Icon: Film },
  art: { tint: 'cat-art', Icon: Palette },
  sport: { tint: 'cat-sport', Icon: Trophy },
  ushqim: { tint: 'cat-ushqim', Icon: UtensilsCrossed },
  nate: { tint: 'cat-nate', Icon: Wine },
  edukim: { tint: 'cat-edukim', Icon: GraduationCap },
  familje: { tint: 'cat-familje', Icon: Users },
}

export function categoryStyle(slug: string | undefined, kind: 'event' | 'deal' = 'event') {
  return (
    (slug ? categories[slug] : undefined) ?? {
      tint: 'cat-default',
      Icon: kind === 'deal' ? Tag : CalendarDays,
    }
  )
}
