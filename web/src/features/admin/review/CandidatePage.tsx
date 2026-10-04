import { useCallback, useState } from 'react'
import { Link, useParams } from 'react-router'
import { ApiError } from '../../../lib/api'
import { t } from '../../../lib/strings'
import { safeHref } from '../../public/format'
import {
  useApprove,
  useCandidate,
  useSaveFields,
  type CandidateDto,
} from './api'
import { FieldsForm, initial, toRequest } from './FieldsForm'
import MergeDialog from './MergeDialog'
import RejectDialog from './RejectDialog'
import { r } from './strings'

const buttonClass =
  'rounded-sm border border-ink px-5 py-2.5 text-sm hover:bg-ink hover:text-paper disabled:opacity-50'

export default function CandidatePage() {
  const { id } = useParams()
  const query = useCandidate(id)

  if (query.isPending)
    return (
      <p role="status" className="text-muted">
        {t.loading}
      </p>
    )
  if (query.error instanceof ApiError && query.error.status === 404)
    return <h1 className="text-3xl">{r.detail.notFound}</h1>
  if (query.isError || !query.data)
    return (
      <div role="alert" className="space-y-3">
        <p>{t.error}</p>
        <button type="button" className={buttonClass} onClick={() => void query.refetch()}>
          {t.retry}
        </button>
      </div>
    )
  return <CandidateView key={query.data.id} candidate={query.data} />
}

function CandidateView({ candidate }: { candidate: CandidateDto }) {
  const [rejecting, setRejecting] = useState(false)
  const closeReject = useCallback(() => setRejecting(false), [])
  const [merging, setMerging] = useState(false)
  const closeMerge = useCallback(() => setMerging(false), [])
  const [mergedDone, setMergedDone] = useState(false)
  const [values, setValues] = useState<Record<string, string>>(() => initial(candidate.fields))
  const save = useSaveFields(candidate.id)
  const [saved, setSaved] = useState(false)
  const approve = useApprove(candidate.id)
  const [published, setPublished] = useState<{ kind: string; itemId: string } | null>(null)
  const open = candidate.status === 'pending'
  // Only auto-rejected candidates are editable; a human rejection (reason set) is final (REQ-067).
  const editable = open || (candidate.status === 'rejected' && candidate.rejectionReason === null)
  const base = initial(candidate.fields)
  const dirty = Object.keys(base).some((k) => values[k] !== base[k])
  const fieldErrors: Record<string, string> =
    save.error instanceof ApiError && save.error.status === 400
      ? Object.fromEntries(
          Object.entries(save.error.problem.errors ?? {}).map(([k, v]) => [k, v[0] ?? t.error]),
        )
      : {}
  const saveError = save.error
    ? save.error instanceof ApiError && save.error.status === 409
      ? r.detail.conflict
      : save.error instanceof ApiError && save.error.status === 400
        ? unshownErrors(save.error, values)
          ? r.detail.saveFailed
          : null
        : r.detail.saveFailed
    : null
  const onSave = () => {
    setSaved(false)
    save.mutate(toRequest(candidate.fields, values), {
      onSuccess: (res) => {
        setValues(initial(res.fields))
        setSaved(true)
      },
    })
  }
  const sourceHref = safeHref(candidate.sourceUrl)

  const approveError = approve.error
    ? approve.error instanceof ApiError && approve.error.status === 400
      ? approveMessage(approve.error)
      : approve.error instanceof ApiError && approve.error.status === 409
        ? r.detail.conflict
        : r.detail.approveFailed
    : null

  return (
    <>
      <h1 className="mb-2 text-3xl">{candidate.title ?? r.queue.untitled}</h1>
      <p className="mb-8 text-sm text-muted">
        {r.kind[candidate.kind]} · {candidate.sourceName} ·{' '}
        {r.detail.decided(r.status[candidate.status])}
      </p>

      <div className="grid gap-8 lg:grid-cols-2">
        <section aria-labelledby="evidence-h" className="min-w-0 space-y-4">
          <h2 id="evidence-h" className="text-xl">
            {r.detail.evidence}
          </h2>
          {sourceHref && (
            <a
              href={sourceHref}
              target="_blank"
              rel="noopener noreferrer"
              className="inline-block underline underline-offset-2"
            >
              {r.detail.openSource}
            </a>
          )}
          {candidate.externalId && (
            <p className="text-sm text-muted">
              {r.detail.externalId}: {candidate.externalId}
            </p>
          )}
          <pre className="max-h-[32rem] overflow-auto whitespace-pre-wrap break-words rounded-sm border border-line bg-surface p-4 text-sm">
            {candidate.evidence}
          </pre>
        </section>

        <section aria-labelledby="fields-h" className="min-w-0 space-y-6">
          <h2 id="fields-h" className="text-xl">
            {r.detail.fields}
          </h2>
          <FieldsForm
            kind={candidate.kind}
            suggestedCategoryName={candidate.suggestedCategory?.name}
            values={values}
            setValues={(v) => {
              setSaved(false)
              save.reset()
              setValues(v)
            }}
            errors={fieldErrors}
            editable={editable}
          />
          <div>
            <h3 id="issues-h" className="mb-2 text-base font-medium">
              {r.detail.issues}
            </h3>
            {candidate.issues.length === 0 ? (
              <p className="text-sm text-muted">{r.detail.noIssues}</p>
            ) : (
              <ul aria-labelledby="issues-h" className="list-disc space-y-1 pl-5 text-sm">
                {candidate.issues.map((i, n) => (
                  <li key={`${i.code}-${n}`}>
                    <span className="font-medium">{i.code}</span>: {i.message}
                  </li>
                ))}
              </ul>
            )}
          </div>
        </section>
      </div>

      {candidate.rejectionReason && (
        <p className="mt-6 text-sm">
          {r.detail.rejectionReason}: {candidate.rejectionReason}
        </p>
      )}

      <div className="mt-8 flex flex-wrap gap-3">
        <button
          type="button"
          disabled={!editable || !dirty || save.isPending}
          onClick={onSave}
          className={buttonClass}
        >
          {r.detail.save}
        </button>
        <button
          type="button"
          disabled={!open || dirty || approve.isPending}
          onClick={() => approve.mutate(undefined, { onSuccess: (res) => setPublished(res) })}
          className={buttonClass}
        >
          {r.detail.approve}
        </button>
        <button
          type="button"
          disabled={!open}
          onClick={() => setRejecting(true)}
          className={buttonClass}
        >
          {r.detail.reject}
        </button>
        <button type="button" disabled={!open} onClick={() => setMerging(true)} className={buttonClass}>
          {r.detail.merge}
        </button>
      </div>

      <div role="status" className="mt-4">
        {saved && !dirty && <p>{r.detail.saved}</p>}
        {mergedDone && <p>{r.detail.merged}</p>}
        {open && dirty && <p className="text-sm text-muted">{r.detail.unsaved}</p>}
        {published && (
          <p>
            {r.detail.published}{' '}
            <Link
              to={`/${published.kind}s/${published.itemId}`}
              className="underline underline-offset-2"
            >
              {r.detail.publicLink}
            </Link>
          </p>
        )}
      </div>
      {saveError && (
        <p role="alert" className="mt-4 text-sm text-danger">
          {saveError}
        </p>
      )}
      {approveError && (
        <p role="alert" className="mt-4 text-sm text-danger">
          {approveError}
        </p>
      )}
      {rejecting && <RejectDialog id={candidate.id} onClose={closeReject} />}
      {merging && (
        <MergeDialog
          candidate={candidate}
          onClose={closeMerge}
          onMerged={() => {
            setMerging(false)
            setMergedDone(true)
          }}
        />
      )}
    </>
  )
}

/** True when a 400 names no rendered field (or none at all), so the reviewer would see nothing. */
function unshownErrors(e: ApiError, values: Record<string, string>): boolean {
  const keys = Object.keys(e.problem.errors ?? {})
  return keys.length === 0 || keys.some((k) => !(k in values))
}

function approveMessage(e: ApiError): string {
  const keys = Object.keys(e.problem.errors ?? {})
  return keys.includes('validation') ? r.detail.notApprovable : r.detail.missing + keys.map((k) => r.fieldKeys[k as keyof typeof r.fieldKeys] ?? k).join(', ')
}

