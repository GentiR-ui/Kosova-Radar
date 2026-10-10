// Times are shown in Europe/Pristina; the IANA zone for Kosovo is Europe/Belgrade (design.md section 4).
const ZONE = 'Europe/Belgrade'

const formatter = new Intl.DateTimeFormat('en-GB', {
  timeZone: ZONE,
  year: 'numeric',
  month: '2-digit',
  day: '2-digit',
  hour: '2-digit',
  minute: '2-digit',
  hourCycle: 'h23',
})

function parts(d: Date): Record<string, string> {
  const out: Record<string, string> = {}
  for (const part of formatter.formatToParts(d))
    if (part.type !== 'literal') out[part.type] = part.value
  return out
}

/** "dd.MM.yyyy, HH:mm" in Pristina time. */
export function formatDateTime(iso: string): string {
  const p = parts(new Date(iso))
  return `${p.day}.${p.month}.${p.year}, ${p.hour}:${p.minute}`
}

/** The start of an event: date and time, or the date and "gjatë ditës" when only the date is known. */
export function formatEventStart(iso: string, timeKnown: boolean): string {
  return timeKnown ? formatDateTime(iso) : `${formatDate(iso)} · gjatë ditës`
}

/** "dd.MM.yyyy" in Pristina time. */
export function formatDate(iso: string): string {
  const p = parts(new Date(iso))
  return `${p.day}.${p.month}.${p.year}`
}

/** The current Pristina date as "yyyy-MM-dd" (the format of the from/to query parameters). */
export function pristinaToday(now: Date = new Date()): string {
  const p = parts(now)
  return `${p.year}-${p.month}-${p.day}`
}

export function formatMoney(amount: number, currency: string | null): string {
  const n = amount.toLocaleString('sq-AL', { minimumFractionDigits: 0, maximumFractionDigits: 2 })
  return currency ? `${n} ${currency}` : n
}

/** Only http(s) URLs may become an href. */
export function safeHref(url: string): string | undefined {
  return /^https?:\/\//i.test(url) ? url : undefined
}

/** An ISO instant as the "yyyy-MM-ddTHH:mm" value of a datetime-local input, in Pristina time. */
export function toPristinaInput(iso: string | null): string {
  if (!iso) return ''
  const p = parts(new Date(iso))
  return `${p.year}-${p.month}-${p.day}T${p.hour}:${p.minute}`
}

/** The inverse of `toPristinaInput`: a "yyyy-MM-ddTHH:mm" Pristina wall time as an ISO instant (null when unparsable). */
export function fromPristinaInput(value: string): string | null {
  const m = /^(\d{4})-(\d{2})-(\d{2})T(\d{2}):(\d{2})$/.exec(value)
  if (!m) return null
  const [y, mo, d, h, mi] = m.slice(1).map(Number) as [number, number, number, number, number]
  const wall = Date.UTC(y, mo - 1, d, h, mi)
  // Two passes settle the zone offset around a DST change.
  let guess = wall
  for (let i = 0; i < 2; i++) {
    const p = parts(new Date(guess))
    const shown = Date.UTC(Number(p.year), Number(p.month) - 1, Number(p.day), Number(p.hour), Number(p.minute))
    guess += wall - shown
  }
  const out = new Date(guess)
  return Number.isNaN(out.getTime()) ? null : out.toISOString()
}
