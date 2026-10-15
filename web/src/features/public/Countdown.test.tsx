import { render, screen } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import Countdown from './Countdown'
import { endOfPristinaDay } from './format'

describe('Countdown', () => {
  afterEach(() => vi.useRealTimers())
  const at = (iso: string) => vi.useFakeTimers({ now: new Date(iso), toFake: ['Date'] })

  it('counts down to the start of an event', () => {
    at('2026-10-13T10:00:00Z')
    render(<Countdown start="2026-10-13T12:30:15Z" end={null} />)
    expect(screen.getByText('Fillon për')).toBeInTheDocument()
    expect(screen.getByText('02:30:15')).toBeInTheDocument()
  })

  it('counts down to the end of a deal', () => {
    at('2026-10-13T10:00:00Z')
    render(<Countdown end="2026-10-13T16:30:45Z" />)
    expect(screen.getByText('Përfundon për')).toBeInTheDocument()
    expect(screen.getByText('06:30:45')).toBeInTheDocument()
  })

  it('shows nothing once the event started, after the end, or for a date-only event', () => {
    at('2026-10-13T13:00:00Z')
    const started = render(<Countdown start="2026-10-13T12:00:00Z" end="2026-10-13T14:00:00Z" />)
    expect(started.container).toBeEmptyDOMElement()
    started.unmount()

    const ended = render(<Countdown start="2026-10-13T10:00:00Z" end="2026-10-13T12:00:00Z" />)
    expect(ended.container).toBeEmptyDOMElement()
    ended.unmount()

    const allDay = render(
      <Countdown start="2026-10-12T22:00:00Z" end="2026-10-13T22:00:00Z" allDay />,
    )
    expect(allDay.container).toBeEmptyDOMElement()
  })

  it('stays hidden while the target is further away than the window', () => {
    at('2026-10-13T10:00:00Z')
    const { container } = render(<Countdown start="2026-10-16T12:00:00Z" end={null} />)
    expect(container).toBeEmptyDOMElement()
  })

  it('shows nothing when there is no date to count', () => {
    at('2026-10-13T10:00:00Z')
    const { container } = render(<Countdown end={null} />)
    expect(container).toBeEmptyDOMElement()
  })

  it('ends an event without an end time at Pristina midnight', () => {
    expect(endOfPristinaDay('2026-10-13T20:00:00Z')).toBe('2026-10-13T22:00:00.000Z') // 22:00 UTC = 00:00 CEST
    expect(endOfPristinaDay('2026-12-01T10:00:00Z')).toBe('2026-12-01T23:00:00.000Z') // winter, UTC+1
  })
})
