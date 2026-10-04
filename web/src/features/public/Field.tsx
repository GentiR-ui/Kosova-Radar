import type { ReactNode } from 'react'

export const selectClass = 'w-full rounded-sm border border-line bg-surface px-3 py-2.5'

export function Field({
  label,
  htmlFor,
  children,
}: {
  label: string
  htmlFor: string
  children: ReactNode
}) {
  return (
    <div>
      <label htmlFor={htmlFor} className="mb-1 block text-sm text-muted">
        {label}
      </label>
      {children}
    </div>
  )
}
