import { useState } from 'react'
import { Link, useParams } from 'react-router'
import { ApiError } from '../../../lib/api'
import { t } from '../../../lib/strings'
import type { Kind } from '../review/api'
import { FieldsForm, initial, toRequest, type Values } from '../review/FieldsForm'
import { r } from '../review/strings'
import { fieldsOf, usePublishedItem, useSaveItem, type PublishedDetail } from './api'
import { p } from './strings'

const buttonClass =
  'rounded-sm border border-ink px-5 py-2.5 text-sm hover:bg-ink hover:text-paper disabled:opacity-50'

export default function PublishedEditPage() {
  const { kind, id } = useParams()
  const valid = kind === 'event' || kind === 'deal'
  const query = usePublishedItem(valid ? kind : 'event', valid ? id : undefined)

  if (!valid || (query.error instanceof ApiError && query.error.status === 404))
    return <h1 className="text-3xl">{p.notFound}</h1>
  if (query.isPending)
    return (
      <p role="status" className="text-muted">
        {t.loading}
      </p>
    )
  if (query.isError || !query.data)
    return (
      <div role="alert" className="space-y-3">
        <p>{t.error}</p>
        <button type="button" className={buttonClass} onClick={() => void query.refetch()}>
          {t.retry}
        </button>
      </div>
    )
  return <EditView key={query.data.id} kind={kind} item={query.data} />
}

function EditView({ kind, item }: { kind: Kind; item: PublishedDetail }) {
  const fields = fieldsOf(item)
  const [values, setValues] = useState<Values>(() => initial(fields))
  const [saved, setSaved] = useState(false)
  const save = useSaveItem(kind, item.id)
  const editable = item.adminStatus !== 'withdrawn'
  const base = initial(fields)
  const dirty = Object.keys(base).some((k) => values[k] !== base[k])

  const error = save.error instanceof ApiError ? save.error : null
  const fieldErrors: Record<string, string> =
    error?.status === 400
      ? Object.fromEntries(
          Object.entries(error.problem.errors ?? {}).map(([k, v]) => [k, v[0] ?? t.error]),
        )
      : {}
  const hiddenError =
    !!save.error &&
    (error?.status === 409
      ? p.conflict
      : error?.status === 400 &&
          Object.keys(fieldErrors).length > 0 &&
          Object.keys(fieldErrors).every((k) => k in values)
        ? null
        : p.saveFailed)

  return (
    <>
      <h1 className="mb-2 text-3xl">{item.title}</h1>
      <p className="mb-8 text-sm text-muted">
        {r.kind[kind]} · {item.sourceName} · {p.adminStatus[item.adminStatus]}
      </p>
      <section aria-labelledby="fields-h" className="max-w-3xl space-y-6">
        <h2 id="fields-h" className="text-xl">
          {r.detail.fields}
        </h2>
        {!editable && <p className="text-sm text-muted">{p.withdrawnReadOnly}</p>}
        <FieldsForm
          kind={kind}
          values={values}
          setValues={(v) => {
            setSaved(false)
            save.reset()
            setValues(v)
          }}
          errors={fieldErrors}
          editable={editable}
        />
      </section>
      <div className="mt-8 flex flex-wrap items-center gap-3">
        <button
          type="button"
          className={buttonClass}
          disabled={!editable || !dirty || save.isPending}
          onClick={() => {
            setSaved(false)
            save.mutate(toRequest(fields, values), {
              onSuccess: (res) => {
                setValues(initial(fieldsOf(res)))
                setSaved(true)
              },
            })
          }}
        >
          {r.detail.save}
        </button>
        <Link to="/admin/published" className="underline underline-offset-2">
          {p.back}
        </Link>
      </div>
      <div role="status" className="mt-4">
        {saved && <p>{p.saved}</p>}
      </div>
      {hiddenError && (
        <p role="alert" className="mt-4 text-sm text-danger">
          {hiddenError}
        </p>
      )}
    </>
  )
}
