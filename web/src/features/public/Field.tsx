import type { ReactNode } from 'react'

export const selectClass =
  'min-h-11 w-full rounded-sm border border-line bg-surface px-3 py-2.5 text-ink transition-colors hover:border-muted focus:border-accent focus:outline-none'

export function Field({
  label,
  htmlFor,
  error,
  children,
}: {
  label: string
  htmlFor: string
  error?: string
  children: ReactNode
}) {
  return (
    <div>
      <label htmlFor={htmlFor} className="mb-1.5 block text-xs font-semibold text-muted">
        {label}
      </label>
      {children}
      {error && (
        <p id={`${htmlFor}-error`} role="alert" className="mt-1 text-sm text-danger">
          {error}
        </p>
      )}
    </div>
  )
}
