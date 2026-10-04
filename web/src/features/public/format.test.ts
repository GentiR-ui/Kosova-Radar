import { describe, expect, it } from 'vitest'
import { formatDate, formatDateTime, fromPristinaInput, pristinaToday } from './format'

describe('Pristina time', () => {
  it('formats summer (UTC+2) and winter (UTC+1) times', () => {
    expect(formatDateTime('2026-07-01T18:00:00Z')).toBe('01.07.2026, 20:00')
    expect(formatDateTime('2026-01-10T18:00:00Z')).toBe('10.01.2026, 19:00')
    expect(formatDate('2026-07-01T22:30:00Z')).toBe('02.07.2026')
  })

  it('rolls over to the next day at Pristina midnight', () => {
    expect(pristinaToday(new Date('2026-01-10T23:30:00Z'))).toBe('2026-01-11')
    expect(pristinaToday(new Date('2026-01-10T22:30:00Z'))).toBe('2026-01-10')
    expect(pristinaToday(new Date('2026-07-10T22:30:00Z'))).toBe('2026-07-11')
  })
})

describe('fromPristinaInput', () => {
  it('converts summer, winter and DST-day wall times to instants', () => {
    expect(fromPristinaInput('2026-07-01T20:00')).toBe('2026-07-01T18:00:00.000Z')
    expect(fromPristinaInput('2026-01-15T20:00')).toBe('2026-01-15T19:00:00.000Z')
    expect(fromPristinaInput('2026-03-29T12:00')).toBe('2026-03-29T10:00:00.000Z')
    expect(fromPristinaInput('')).toBeNull()
  })
})
