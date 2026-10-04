import { Link, useSearchParams } from 'react-router'
import { Field, selectClass } from '../../public/Field'
import { flat } from '../../public/api'
import ListShell from '../../public/ListShell'
import { useCandidates, useSourceOptions, type QueueFilters } from './api'
import { r } from './strings'

const q = r.queue

const statusOptions = [
  ['all', q.all],
  ['pending', r.status.pending],
  ['rejected', r.status.rejected],
  ['published', r.status.published],
  ['merged', r.status.merged],
] as const

const validationOptions = [
  ['', q.all],
  ['valid', r.validation.valid],
  ['needsReview', r.validation.needsReview],
  ['rejected', r.validation.rejected],
] as const

const kindOptions = [
  ['', q.all],
  ['event', r.kind.event],
  ['deal', r.kind.deal],
] as const

const labelFor = (map: Record<string, string>, key: string) => map[key] ?? key

export default function ReviewQueuePage() {
  const [params, setParams] = useSearchParams()
  // No status in the URL means the default queue ("Në pritje"); "all" is explicit.
  const status = params.get('status') ?? 'pending'
  const validation = params.get('validation') ?? ''
  const kind = params.get('kind') ?? ''
  const sourceId = params.get('sourceId') ?? ''

  const filters: QueueFilters = {
    status: status === 'all' ? undefined : status,
    validation: validation || undefined,
    kind: kind || undefined,
    sourceId: sourceId || undefined,
  }
  const candidates = useCandidates(filters)
  const sources = useSourceOptions()

  const set = (key: string, value: string) =>
    setParams(
      (prev) => {
        const next = new URLSearchParams(prev)
        if (value) next.set(key, value)
        else next.delete(key)
        return next
      },
      { replace: true },
    )

  return (
    <>
      <h1 className="mb-8 text-3xl">{q.title}</h1>
      <div className="mb-8 grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
        <Field label={q.status} htmlFor="f-status">
          <select
            id="f-status"
            className={selectClass}
            value={status}
            onChange={(e) => set('status', e.target.value)}
          >
            {statusOptions.map(([v, label]) => (
              <option key={v} value={v}>
                {label}
              </option>
            ))}
          </select>
        </Field>
        <Field label={q.validation} htmlFor="f-validation">
          <select
            id="f-validation"
            className={selectClass}
            value={validation}
            onChange={(e) => set('validation', e.target.value)}
          >
            {validationOptions.map(([v, label]) => (
              <option key={v} value={v}>
                {label}
              </option>
            ))}
          </select>
        </Field>
        <Field label={q.kind} htmlFor="f-kind">
          <select
            id="f-kind"
            className={selectClass}
            value={kind}
            onChange={(e) => set('kind', e.target.value)}
          >
            {kindOptions.map(([v, label]) => (
              <option key={v} value={v}>
                {label}
              </option>
            ))}
          </select>
        </Field>
        <Field label={q.source} htmlFor="f-source">
          <select
            id="f-source"
            className={selectClass}
            value={sourceId}
            onChange={(e) => set('sourceId', e.target.value)}
          >
            <option value="">{q.all}</option>
            {sources.data?.map((s) => (
              <option key={s.id} value={s.id}>
                {s.name}
              </option>
            ))}
          </select>
        </Field>
      </div>

      <ListShell queries={[candidates]}>
        <div className="overflow-x-auto">
          <table className="w-full text-left text-sm">
            <caption className="sr-only">{q.list}</caption>
            <thead>
              <tr className="border-b border-line text-muted">
                <th scope="col" className="py-2 pr-4 font-medium">
                  {q.columns.title}
                </th>
                <th scope="col" className="py-2 pr-4 font-medium">
                  {q.columns.source}
                </th>
                <th scope="col" className="py-2 pr-4 font-medium">
                  {q.columns.validation}
                </th>
                <th scope="col" className="py-2 font-medium">
                  {q.columns.status}
                </th>
              </tr>
            </thead>
            <tbody>
              {flat(candidates).map((c) => (
                <tr key={c.id} className="border-b border-line">
                  <td className="py-3 pr-4">
                    <Link to={`/admin/items/${c.id}`} className="underline underline-offset-2">
                      {c.title ?? q.untitled}
                    </Link>
                  </td>
                  <td className="py-3 pr-4">{c.sourceName}</td>
                  <td className="py-3 pr-4">{labelFor(r.validation, c.validation)}</td>
                  <td className="py-3">{labelFor(r.status, c.status)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </ListShell>
    </>
  )
}
