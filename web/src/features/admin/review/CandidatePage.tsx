import { useCallback, useState, type ReactNode } from 'react'
import { Link, useParams } from 'react-router'
import { ApiError } from '../../../lib/api'
import { t } from '../../../lib/strings'
import { Field, selectClass } from '../../public/Field'
import { useCategories, useCities } from '../../public/api'
import { fromPristinaInput, safeHref, toPristinaInput } from '../../public/format'
import {
  useApprove,
  useCandidate,
  useSaveFields,
  type CandidateDto,
  type CandidateFields,
  type FieldsRequest,
} from './api'
import MergeDialog from './MergeDialog'
import RejectDialog from './RejectDialog'
import { r } from './strings'

const inputClass = 'w-full rounded-sm border border-line bg-surface px-3 py-2.5'
const buttonClass =
  'rounded-sm border border-ink px-5 py-2.5 text-sm hover:bg-ink hover:text-paper disabled:opacity-50'

const text = (v: string | number | null) => (v === null ? '' : String(v))

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
            candidate={candidate}
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

type Values = Record<string, string>

function FieldsForm({
  candidate,
  values,
  setValues,
  errors,
  editable,
}: {
  candidate: CandidateDto
  values: Values
  setValues: (v: Values) => void
  errors: Record<string, string>
  editable: boolean
}) {
  const deal = candidate.kind === 'deal'
  const cities = useCities()
  const categories = useCategories(candidate.kind)
  const bind = (key: string) => ({
    id: `fld-${key}`,
    value: values[key] ?? '',
    onChange: (e: { target: { value: string } }) => setValues({ ...values, [key]: e.target.value }),
    'aria-invalid': errors[key] ? true : undefined,
    'aria-describedby': errors[key] ? `fld-${key}-error` : undefined,
  })
  const field = (key: string, label: string, type = 'text', extra: object = {}): ReactNode => (
    <Field label={label} htmlFor={`fld-${key}`} error={errors[key]}>
      <input type={type} className={inputClass} {...bind(key)} {...extra} />
    </Field>
  )
  const money = { step: 'any', min: 0, inputMode: 'decimal' as const }

  return (
    // "Aprovo" stays disabled while the form differs from the saved fields, so it never publishes unsaved edits.
    <fieldset disabled={!editable} className="grid gap-4 border-0 p-0 sm:grid-cols-2">
      <div className="sm:col-span-2">{field('title', r.fields.title)}</div>
      <div className="sm:col-span-2">
        <Field label={r.fields.description} htmlFor="fld-description" error={errors.description}>
          <textarea rows={5} className={inputClass} {...bind('description')} />
        </Field>
      </div>
      <div className="sm:col-span-2">{field('url', r.fields.url, 'url')}</div>
      <Field label={r.fields.city} htmlFor="fld-citySlug" error={errors.citySlug}>
        <select className={selectClass} {...bind('citySlug')}>
          <option value="">{r.fields.none}</option>
          {cities.data?.map((c) => (
            <option key={c.slug} value={c.slug}>
              {c.name}
            </option>
          ))}
        </select>
      </Field>
      {!deal && field('venueName', r.fields.venue)}
      <Field label={r.fields.category} htmlFor="fld-categorySlug" error={errors.categorySlug}>
        <select className={selectClass} {...bind('categorySlug')}>
          <option value="">{candidate.suggestedCategory?.name ?? r.fields.none}</option>
          {categories.data?.map((c) => (
            <option key={c.slug} value={c.slug}>
              {c.name}
            </option>
          ))}
        </select>
      </Field>
      {deal ? (
        <>
          {field('merchantName', r.fields.merchant)}
          {field('oldPrice', r.fields.oldPrice, 'number', money)}
          {field('newPrice', r.fields.newPrice, 'number', money)}
          {field('validFrom', r.fields.validFrom, 'datetime-local')}
          {field('validUntil', r.fields.validUntil, 'datetime-local')}
        </>
      ) : (
        <>
          {field('startsAt', r.fields.startsAt, 'datetime-local')}
          {field('endsAt', r.fields.endsAt, 'datetime-local')}
        </>
      )}
      {field('priceText', r.fields.priceText)}
      {!deal && field('priceAmount', r.fields.priceAmount, 'number', money)}
      {field('currency', r.fields.currency, 'text', { maxLength: 3 })}
    </fieldset>
  )
}

function initial(f: CandidateFields): Record<string, string> {
  return {
    title: text(f.title),
    description: text(f.description),
    url: text(f.url),
    citySlug: text(f.citySlug),
    venueName: text(f.venueName),
    categorySlug: text(f.categorySlug),
    merchantName: text(f.merchantName),
    startsAt: toPristinaInput(f.startsAt),
    endsAt: toPristinaInput(f.endsAt),
    validFrom: toPristinaInput(f.validFrom),
    validUntil: toPristinaInput(f.validUntil),
    priceText: text(f.priceText),
    priceAmount: text(f.priceAmount),
    oldPrice: text(f.oldPrice),
    newPrice: text(f.newPrice),
    currency: text(f.currency),
  }
}

const trimOrNull = (v: string) => v.trim() || null
const num = (v: string) => (v.trim() === '' ? null : Number(v))

/**
 * The save request: the saved fields with the edited form values on top. Untouched fields keep their saved
 * value (so seconds and ambiguous DST times are not rewritten); imageUrl and the stated discount pass through.
 */
function toRequest(f: CandidateFields, v: Values): FieldsRequest {
  const { cityText: _c, categoryText: _k, discountPercent: _d, ...rest } = f
  const base = initial(f)
  const out: FieldsRequest = { ...rest }
  const set = <K extends keyof FieldsRequest>(key: K, convert: (x: string) => FieldsRequest[K]) => {
    const value = v[key] ?? ''
    if (value !== base[key]) out[key] = convert(value)
  }
  for (const k of ['title', 'description', 'url', 'citySlug', 'venueName', 'categorySlug', 'merchantName', 'priceText', 'currency'] as const)
    set(k, trimOrNull)
  for (const k of ['startsAt', 'endsAt', 'validFrom', 'validUntil'] as const) set(k, fromPristinaInput)
  for (const k of ['priceAmount', 'oldPrice', 'newPrice'] as const) set(k, num)
  return out
}
