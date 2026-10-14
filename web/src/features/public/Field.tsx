import type { ReactNode } from 'react'

export const selectClass =
  'min-h-12 w-full rounded-xl border border-line bg-soft px-3.5 py-2.5 text-[0.95rem] text-ink transition-[border-color,background-color,box-shadow] duration-200 hover:border-accent/40 focus:border-accent focus:bg-surface focus:shadow-[0_0_0_4px_rgb(4_120_87/0.12)] focus:outline-none'

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
      <label htmlFor={htmlFor} className="mb-1.5 block text-xs font-bold text-muted">
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
