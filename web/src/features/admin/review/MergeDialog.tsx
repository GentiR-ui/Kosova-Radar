import { useEffect, useRef, useState } from 'react'
import { ApiError } from '../../../lib/api'
import { t } from '../../../lib/strings'
import { formatDate } from '../../public/format'
import { useMerge, useMergeTargets, type CandidateDto } from './api'
import { r } from './strings'

const s = r.mergeDialog

export default function MergeDialog({
  candidate,
  onClose,
  onMerged,
}: {
  candidate: CandidateDto
  onClose: () => void
  onMerged: () => void
}) {
  const [search, setSearch] = useState('')
  const [term, setTerm] = useState('')
  const targets = useMergeTargets(candidate.kind, term)
  const merge = useMerge(candidate.id)
  const ref = useRef<HTMLInputElement>(null)
  const suggested =
    candidate.duplicateOf && !candidate.duplicateOf.isCandidate ? candidate.duplicateOf : null

  const results = (targets.data?.items ?? []).filter((i) => i.id !== suggested?.id)

  useEffect(() => {
    ref.current?.focus()
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') onClose()
    }
    document.addEventListener('keydown', onKey)
    return () => document.removeEventListener('keydown', onKey)
  }, [onClose])

  useEffect(() => {
    const id = setTimeout(() => setTerm(search.trim()), 300)
    return () => clearTimeout(id)
  }, [search])

  const error = merge.error
    ? merge.error instanceof ApiError && merge.error.status === 409
      ? r.detail.conflict
      : merge.error instanceof ApiError && merge.error.status === 400
        ? s.invalid
        : s.failed
    : null
  const choose = (targetId: string) => merge.mutate({ targetId }, { onSuccess: onMerged })
  const confirmButton = (targetId: string) => (
    <button
      type="button"
      disabled={merge.isPending}
      onClick={() => choose(targetId)}
      className="shrink-0 rounded-sm border border-ink px-4 py-2 text-sm hover:bg-ink hover:text-paper disabled:opacity-50"
    >
      {s.confirm}
    </button>
  )

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-ink/40 p-4">
      <div
        role="dialog"
        aria-modal="true"
        aria-labelledby="merge-title"
        className="max-h-full w-full max-w-lg space-y-4 overflow-auto rounded-md bg-paper p-6 shadow-lg"
      >
        <h2 id="merge-title" className="text-xl">
          {s.title}
        </h2>
        {suggested && (
          <div className="space-y-2">
            <h3 className="text-sm font-medium">{s.suggested}</h3>
            <div className="flex items-center justify-between gap-3">
              <span className="min-w-0 break-words">{suggested.title ?? r.queue.untitled}</span>
              {confirmButton(suggested.id)}
            </div>
          </div>
        )}
        <div className="space-y-1.5">
          <label htmlFor="merge-search" className="block text-sm font-medium">
            {s.search}
          </label>
          <input
            id="merge-search"
            ref={ref}
            type="search"
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            className="w-full rounded-md border border-line bg-surface px-3 py-2.5"
          />
        </div>
        {targets.isError ? (
          <div role="alert" className="space-y-2 text-sm">
            <p className="text-danger">{t.error}</p>
            <button type="button" onClick={() => void targets.refetch()} className="underline">
              {t.retry}
            </button>
          </div>
        ) : targets.isPending ? (
          <p role="status" className="text-sm text-muted">
            {t.loading}
          </p>
        ) : results.length === 0 ? (
          <p className="text-sm text-muted">{s.none}</p>
        ) : (
          <ul aria-label={s.results} className="divide-y divide-line">
            {results.map((i) => (
              <li key={i.id} className="flex items-center justify-between gap-3 py-2">
                <span className="min-w-0 break-words">
                  {i.title}
                  <span className="block text-sm text-muted">
                    {[i.cityName, i.date && formatDate(i.date), i.sourceName].filter(Boolean).join(' · ')}
                  </span>
                </span>
                {confirmButton(i.id)}
              </li>
            ))}
          </ul>
        )}
        {error && (
          <p role="alert" className="text-sm text-danger">
            {error}
          </p>
        )}
        <div className="flex justify-end">
          <button
            type="button"
            onClick={onClose}
            className="rounded-sm border border-line px-5 py-2.5 text-sm"
          >
            {s.cancel}
          </button>
        </div>
      </div>
    </div>
  )
}
