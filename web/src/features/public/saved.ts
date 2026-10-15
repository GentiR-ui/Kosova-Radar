import { useSyncExternalStore } from 'react'

/**
 * "Të ruajturat": the items a visitor bookmarked, kept in this browser only (no account, no API).
 * One store so every card and the feed toggle react to the same list.
 */
const KEY = 'kosova-radar.saved.v1'

function read(): string[] {
  try {
    const raw = JSON.parse(localStorage.getItem(KEY) ?? '[]') as unknown
    return Array.isArray(raw) ? raw.filter((v): v is string => typeof v === 'string') : []
  } catch {
    return [] // private mode, blocked storage or a broken value: start empty
  }
}

let items = typeof localStorage === 'undefined' ? [] : read()
const listeners = new Set<() => void>()

function publish(next: string[]) {
  items = next
  try {
    localStorage.setItem(KEY, JSON.stringify(next))
  } catch {
    /* storage full or blocked: the list still works for this page view */
  }
  listeners.forEach((l) => l())
}

function subscribe(listener: () => void) {
  listeners.add(listener)
  return () => listeners.delete(listener)
}

export const savedKey = (kind: 'event' | 'deal', id: string) => `${kind}:${id}`

export function useSaved() {
  const list = useSyncExternalStore(
    subscribe,
    () => items,
    () => items,
  )
  return {
    list,
    count: list.length,
    has: (key: string) => list.includes(key),
    toggle: (key: string) =>
      publish(list.includes(key) ? list.filter((v) => v !== key) : [...list, key]),
  }
}
