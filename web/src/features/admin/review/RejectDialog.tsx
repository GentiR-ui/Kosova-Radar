import { useEffect, useRef, useState, type FormEvent } from 'react'
import { t } from '../../../lib/strings'
import { ApiError } from '../../../lib/api'
import { useReject } from './api'
import { r } from './strings'

const s = r.rejectDialog

export default function RejectDialog({ id, onClose }: { id: string; onClose: () => void }) {
  const [reason, setReason] = useState('')
  const reject = useReject(id)
  const ref = useRef<HTMLTextAreaElement>(null)

  useEffect(() => {
    ref.current?.focus()
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') onClose()
    }
    document.addEventListener('keydown', onKey)
    return () => document.removeEventListener('keydown', onKey)
  }, [onClose])

  const trimmed = reason.trim()
  const invalid = trimmed.length === 0 || trimmed.length > 500
  const error = reject.error
    ? (error409(reject.error) ??
      (reject.error instanceof ApiError && reject.error.status === 400 ? s.required : t.error))
    : null

  const onSubmit = (e: FormEvent) => {
    e.preventDefault()
    if (invalid) return
    reject.mutate({ reason: trimmed }, { onSuccess: onClose })
  }

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-ink/40 p-4">
      <form
        role="dialog"
        aria-modal="true"
        aria-labelledby="reject-title"
        onSubmit={onSubmit}
        className="w-full max-w-md space-y-4 rounded-md bg-paper p-6 shadow-lg"
      >
        <h2 id="reject-title" className="text-xl">
          {r.detail.reject}
        </h2>
        <div className="space-y-1.5">
          <label htmlFor="reject-reason" className="block text-sm font-medium">
            {s.reason}
          </label>
          <textarea
            id="reject-reason"
            ref={ref}
            rows={4}
            maxLength={500}
            value={reason}
            onChange={(e) => setReason(e.target.value)}
            className="w-full rounded-md border border-line bg-surface px-3 py-2.5"
          />
        </div>
        {error && (
          <p role="alert" className="text-sm text-danger">
            {error}
          </p>
        )}
        <div className="flex justify-end gap-3">
          <button
            type="button"
            onClick={onClose}
            className="rounded-sm border border-line px-5 py-2.5 text-sm"
          >
            {s.cancel}
          </button>
          <button
            type="submit"
            disabled={invalid || reject.isPending}
            className="rounded-sm border border-ink bg-ink px-5 py-2.5 text-sm text-paper disabled:opacity-50"
          >
            {s.confirm}
          </button>
        </div>
      </form>
    </div>
  )
}

function error409(e: unknown): string | null {
  return e instanceof ApiError && e.status === 409 ? r.detail.conflict : null
}
