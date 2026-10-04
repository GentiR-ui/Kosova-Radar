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
