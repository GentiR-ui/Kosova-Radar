import { Timer } from 'lucide-react'
import { useSyncExternalStore } from 'react'
import { p } from './strings'

const HOUR_MS = 3_600_000
const DAY_MS = 24 * HOUR_MS

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

/** Below a day the strip ticks every second; further out it only says how long is left. */
function left(ms: number): string {
  if (ms >= DAY_MS) {
    const days = Math.floor(ms / DAY_MS)
    return days === 1 ? p.oneDay : p.days_(days)
  }
  const s = Math.floor(ms / 1000)
  return [Math.floor(s / 3600), Math.floor(s / 60) % 60, s % 60]
    .map((v) => String(v).padStart(2, '0'))
    .join(':')
}

/**
 * Live "starts in" / "ends in" strip on a card. `start` (events) counts down to the start and then
 * shows "started" until `end`; without `start` (deals) it counts down to `end`. `allDay` = only the
 * date is known. Nothing to count (a deal without an end date) renders nothing.
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
  if (startMs === null && endMs === null) return null

  let label: string
  let digits: string | null = null
  let ended = false
  if (endMs !== null && current >= endMs) {
    label = p.ended
    ended = true
  } else if (allDay) label = p.allDay
  else if (startMs !== null && current < startMs) {
    label = p.startsIn
    digits = left(startMs - current)
  } else if (startMs !== null) label = p.started
  else if (endMs !== null) {
    label = p.endsIn
    digits = left(endMs - current)
  } else {
    label = p.ended
    ended = true
  }

  return (
    <p
      className={`countdown ${tone === 'deal' ? 'countdown-deal' : ''} ${ended ? 'countdown-ended' : ''}`}
    >
      <span className="countdown-label">
        <Timer aria-hidden="true" size={13} />
        {label}
      </span>
      {digits && (
        // The ticking digits are not announced every second.
        <span aria-hidden="true" className="countdown-digits">
          {digits}
        </span>
      )}
    </p>
  )
}
