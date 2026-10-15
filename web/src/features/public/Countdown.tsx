import { Timer } from 'lucide-react'
import { useSyncExternalStore } from 'react'
import { p } from './strings'

const HOUR_MS = 3_600_000
export const DAY_MS = 24 * HOUR_MS

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
 * Live "starts in" / "ends in" chip. `start` (events) counts down to the start; without `start`
 * (deals) it counts down to `end`. It only appears while the target is within `within`, so cards
 * show urgency without a clock on every item; with nothing to count it renders nothing.
 */
export default function Countdown({
  start,
  end,
  allDay = false,
  within = DAY_MS,
}: {
  start?: string
  end: string | null
  allDay?: boolean
  /** Show the chip only while the live countdown is below this many milliseconds. */
  within?: number
}) {
  const current = useNow()
  const startMs = start ? Date.parse(start) : null
  const endMs = end ? Date.parse(end) : null
  if (startMs === null && endMs === null) return null
  if (allDay) return null
  if (endMs !== null && current >= endMs) return null

  let label: string
  let remaining: number | null = null
  if (startMs !== null && current < startMs) {
    label = p.startsIn
    remaining = startMs - current
  } else if (startMs !== null) {
    label = p.started
  } else {
    label = p.endsIn
    remaining = endMs! - current
  }

  if (remaining === null || remaining > within) return null

  return (
    <span className="badge badge-soft w-fit">
      <Timer aria-hidden="true" className="size-3.5" />
      {label}
      {/* The ticking digits are not announced every second. */}
      <span aria-hidden="true" className="tnum font-extrabold">
        {hms(remaining)}
      </span>
    </span>
  )
}
