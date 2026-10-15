import { useEffect, useRef } from 'react'

interface Props {
  title: string
  confirmLabel: string
  cancelLabel: string
  onConfirm: () => void
  onClose: () => void
}

/** In-page confirmation for destructive actions (ADR-037). Focus goes to "Anulo"; the caller restores it on close. */
export default function ConfirmDialog({
  title,
  confirmLabel,
  cancelLabel,
  onConfirm,
  onClose,
}: Props) {
  const cancel = useRef<HTMLButtonElement>(null)
  // Callers pass inline callbacks; keep the latest one in a ref so re-renders do not re-run the focus effect.
  const close = useRef(onClose)
  close.current = onClose

  useEffect(() => {
    const trigger = document.activeElement as HTMLElement | null
    cancel.current?.focus()
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') close.current()
    }
    document.addEventListener('keydown', onKey)
    return () => {
      document.removeEventListener('keydown', onKey)
      if (trigger && document.contains(trigger)) trigger.focus()
    }
  }, [])

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-ink/40 p-4">
      <div
        role="dialog"
        aria-modal="true"
        aria-labelledby="confirm-title"
        className="w-full max-w-md space-y-4 rounded-md bg-paper p-6 shadow-lg"
      >
        <h2 id="confirm-title" className="text-xl">
          {title}
        </h2>
        <div className="flex justify-end gap-3">
          <button
            ref={cancel}
            type="button"
            onClick={onClose}
            className="rounded-sm border border-line px-5 py-2.5 text-sm"
          >
            {cancelLabel}
          </button>
          <button
            type="button"
            onClick={onConfirm}
            className="rounded-sm border border-ink bg-ink px-5 py-2.5 text-sm text-paper"
          >
            {confirmLabel}
          </button>
        </div>
      </div>
    </div>
  )
}
