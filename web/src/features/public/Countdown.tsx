import { useSyncExternalStore } from 'react'
import { p } from './strings'

export const DAY_MS = 86_400_000

// One shared one-second clock for every countdown on the page.
let now = Date.now()
let timer: ReturnType<typeof setInterval> | undefined
const listeners = new Set<() => void>()

function subscribe(listener: () => void) {
  listeners.add(listener)
  if (!timer) {
    now = Date.now()
    timer = setInterval(() => {
      now = Date.now()
      listeners.forEach((l) => l())
    }, 1000)
  }
  return () => {
    listeners.delete(listener)
    if (listeners.size === 0) {
      clearInterval(timer)
      timer = undefined
    }
  }
}

const useNow = () => useSyncExternalStore(subscribe, () => now)

function hms(ms: number): string {
  const s = Math.floor(ms / 1000)
  return [Math.floor(s / 3600), Math.floor(s / 60) % 60, s % 60]
    .map((v) => String(v).padStart(2, '0'))
    .join(':')
}

/**
 * Live "starts in" / "ends in" strip. `start` (events) counts down to the start and then shows
 * "started" until `end`; without `start` (deals) it counts down to `end`. `allDay` = only the date is
 * known. With `within`, the strip appears only while a live countdown is below that many milliseconds —
 * that is how cards show urgency without a clock on every item.
 */
export default function Countdown({
  start,
  end,
  allDay = false,
  within,
  tone,
}: {
  start?: string
  end: string | null
  allDay?: boolean
  within?: number
  tone: 'event' | 'deal'
}) {
  const current = useNow()
  const startMs = start ? Date.parse(start) : null
  const endMs = end ? Date.parse(end) : null
  let label: string
  let remaining: number | null = null
  if (endMs !== null && current >= endMs) label = p.ended
  else if (allDay) label = p.allDay
  else if (startMs !== null && current < startMs) {
    label = p.startsIn
    remaining = startMs - current
  } else if (startMs !== null) label = p.started
  else if (endMs !== null) {
    label = p.endsIn
    remaining = endMs - current
  } else label = p.ended

  if (within !== undefined && (remaining === null || remaining > within)) return null

  return (
    <span
      className={`inline-flex items-center gap-2 rounded-full bg-surface/95 px-2.5 py-1.5 text-[11px] font-bold shadow-card backdrop-blur-sm ${
        tone === 'event' ? 'text-event' : 'text-accent'
      }`}
    >
      <span aria-hidden="true" className="live-dot" />
      {label}
      {remaining !== null && (
        // The ticking digits are not announced every second.
        <span aria-hidden="true" className="font-mono tabular-nums text-ink">
          {hms(remaining)}
        </span>
      )}
    </span>
  )
}
