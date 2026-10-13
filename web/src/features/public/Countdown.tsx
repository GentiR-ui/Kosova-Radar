import { Timer } from 'lucide-react'
import { useSyncExternalStore } from 'react'
import { p } from './strings'

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
 * Live "starts in" / "ends in" strip on a card. `start` (events) counts down to the start and then shows
 * "started" until `end`; without `start` (deals) it counts down to `end`. `allDay` = only the date is known.
 */
export default function Countdown({
  start,
  end,
  allDay = false,
  tone,
}: {
  start?: string
  end: string | null
  allDay?: boolean
  tone: 'event' | 'deal'
}) {
  const current = useNow()
  const startMs = start ? Date.parse(start) : null
  const endMs = end ? Date.parse(end) : null
  let label: string
  let digits: string | null = null
  if (endMs !== null && current >= endMs) label = p.ended
  else if (allDay) label = p.allDay
  else if (startMs !== null && current < startMs) {
    label = p.startsIn
    digits = hms(startMs - current)
  } else if (startMs !== null) label = p.started
  else if (endMs !== null) {
    label = p.endsIn
    digits = hms(endMs - current)
  } else label = p.ended

  const colours = tone === 'event' ? 'bg-event-soft text-event' : 'bg-accent-soft text-accent'
  return (
    <div
      className={`grid grid-cols-[minmax(0,1fr)_auto] items-center gap-2 rounded-sm px-3 py-2 ${colours}`}
    >
      <span className="flex items-center gap-1.5 text-xs font-semibold">
        <Timer aria-hidden="true" className="size-3.5" />
        {label}
      </span>
      {/* The ticking digits are not announced every second. */}
      <span
        aria-hidden="true"
        className="min-w-16 text-right font-mono text-sm font-bold tabular-nums"
      >
        {digits ?? '—'}
      </span>
    </div>
  )
}
