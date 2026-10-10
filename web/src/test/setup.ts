import '@testing-library/jest-dom/vitest'
import { cleanup } from '@testing-library/react'
import { afterEach, beforeEach, vi } from 'vitest'

// Vitest runs without globals, so Testing Library cannot register its automatic cleanup.
afterEach(() => {
  cleanup()
  vi.unstubAllGlobals()
})

// jsdom has no IntersectionObserver (Motion's whileInView needs it): report every target as visible.
beforeEach(() => {
  vi.stubGlobal(
    'IntersectionObserver',
    class {
      callback: (entries: unknown[], observer: unknown) => void
      constructor(callback: (entries: unknown[], observer: unknown) => void) {
        this.callback = callback
      }
      observe(target: Element) {
        this.callback([{ isIntersecting: true, target, intersectionRatio: 1 }], this)
      }
      unobserve() {}
      disconnect() {}
      takeRecords() {
        return []
      }
    },
  )
})
