import { render, screen } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import Countdown from './Countdown'
import { endOfPristinaDay } from './format'

describe('Countdown', () => {
  afterEach(() => vi.useRealTimers())
  const at = (iso: string) => vi.useFakeTimers({ now: new Date(iso), toFake: ['Date'] })

  it('counts down to the start of an event', () => {
    at('2026-10-13T10:00:00Z')
    render(<Countdown tone="event" start="2026-10-13T12:30:15Z" end={null} />)
    expect(screen.getByText('Fillon për')).toBeInTheDocument()
    expect(screen.getByText('02:30:15')).toBeInTheDocument()
  })

  it('shows started, then ended after the end', () => {
    at('2026-10-13T13:00:00Z')
    const { unmount } = render(
      <Countdown tone="event" start="2026-10-13T12:00:00Z" end="2026-10-13T14:00:00Z" />,
    )
    expect(screen.getByText('Ka filluar')).toBeInTheDocument()
    at('2026-10-13T15:00:00Z')
    unmount() // the shared clock re-reads the time on the next mount
    render(<Countdown tone="event" start="2026-10-13T12:00:00Z" end="2026-10-13T14:00:00.001Z" />)
    expect(screen.getByText('Ka përfunduar')).toBeInTheDocument()
  })

  it('counts down to the end of a deal and labels date-only events', () => {
    at('2026-10-13T10:00:00Z')
    render(<Countdown tone="deal" end="2026-10-14T10:00:00Z" />)
    expect(screen.getByText('Përfundon për')).toBeInTheDocument()
    expect(screen.getByText('24:00:00')).toBeInTheDocument()
    render(
      <Countdown tone="event" start="2026-10-12T22:00:00Z" end="2026-10-13T22:00:00Z" allDay />,
    )
    expect(screen.getByText('Gjatë ditës')).toBeInTheDocument()
  })

  it('ends an event without an end time at Pristina midnight', () => {
    expect(endOfPristinaDay('2026-10-13T20:00:00Z')).toBe('2026-10-13T22:00:00.000Z') // 22:00 UTC = 00:00 CEST
    expect(endOfPristinaDay('2026-12-01T10:00:00Z')).toBe('2026-12-01T23:00:00.000Z') // winter, UTC+1
  })
})
