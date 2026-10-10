import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import ListShell from './ListShell'

const done = {
  isError: false,
  isPending: false,
  data: { pages: [{ items: [1], page: 1, pageSize: 20, total: 1 }] },
  hasNextPage: false,
  isFetchingNextPage: false,
  fetchNextPage: () => Promise.resolve(),
  refetch: () => Promise.resolve(),
} as never

describe('ListShell layout', () => {
  it('renders children as a card grid only when asked', () => {
    const { rerender } = render(
      <ListShell queries={[done]}>
        <p>kid</p>
      </ListShell>,
    )
    expect(screen.getByText('kid').parentElement?.className ?? '').not.toContain('grid')
    rerender(
      <ListShell queries={[done]} grid>
        <p>kid</p>
      </ListShell>,
    )
    expect(screen.getByText('kid').parentElement).toHaveClass('grid')
  })
})
