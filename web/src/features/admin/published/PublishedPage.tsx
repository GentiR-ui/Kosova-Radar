import { Link, useSearchParams } from 'react-router'
import { ApiError } from '../../../lib/api'
import { Field, selectClass } from '../../public/Field'
import { flat } from '../../public/api'
import ListShell from '../../public/ListShell'
import { formatDate } from '../../public/format'
import { r } from '../review/strings'
import { useItemAction, usePublishedItems, type PublishedFilters, type PublishedItemDto } from './api'
import { p } from './strings'

const buttonClass =
  'rounded-sm border border-ink px-3 py-1.5 text-sm hover:bg-ink hover:text-paper disabled:opacity-50'

type Options = readonly (readonly [string, string])[]

const statusOptions: Options = [
  ['', p.all],
  ['published', p.adminStatus.published],
  ['expired', p.adminStatus.expired],
  ['withdrawn', p.adminStatus.withdrawn],
]
const kindOptions: Options = [
  ['', p.all],
  ['event', r.kind.event],
  ['deal', r.kind.deal],
]

export default function PublishedPage() {
  const [params, setParams] = useSearchParams()
  const status = params.get('status') ?? ''
  const kind = params.get('kind') ?? ''
  const filters: PublishedFilters = { status: status || undefined, kind: kind || undefined }
  const items = usePublishedItems(filters)
  const expire = useItemAction('expire')
  const withdraw = useItemAction('withdraw')
  const failure = expire.error ?? withdraw.error

  const act = (m: typeof expire, item: PublishedItemDto) => {
    expire.reset()
    withdraw.reset()
    m.mutate(item)
  }
  const set = (key: string, value: string) => {
    expire.reset()
    withdraw.reset()
    setParams(
      (prev) => {
        const next = new URLSearchParams(prev)
        if (value) next.set(key, value)
        else next.delete(key)
        return next
      },
      { replace: true },
    )
  }
  const select = (key: string, label: string, value: string, options: Options) => (
    <Field label={label} htmlFor={`f-${key}`}>
      <select
        id={`f-${key}`}
        className={selectClass}
        value={value}
        onChange={(e) => set(key, e.target.value)}
      >
        {options.map(([v, text]) => (
          <option key={v} value={v}>
            {text}
          </option>
        ))}
      </select>
    </Field>
  )

  return (
    <>
      <h1 className="mb-8 text-3xl">{p.title}</h1>
      <div className="mb-8 grid gap-4 sm:grid-cols-2 lg:max-w-xl">
        {select('kind', p.kind, kind, kindOptions)}
        {select('status', p.status, status, statusOptions)}
      </div>

      {failure && (
        <p role="alert" className="mb-4 text-sm text-danger">
          {failure instanceof ApiError && failure.status === 409
            ? p.actionConflict
            : p.actionFailed}
        </p>
      )}

      <ListShell queries={[items]}>
        <div className="overflow-x-auto">
          <table className="w-full text-left text-sm">
            <caption className="sr-only">{p.list}</caption>
            <thead>
              <tr className="border-b border-line text-muted">
                {(['title', 'kind', 'status', 'source', 'actions'] as const).map((c) => (
                  <th key={c} scope="col" className="py-2 pr-4 font-medium">
                    {p.columns[c]}
                  </th>
                ))}
              </tr>
            </thead>
            <tbody>
              {flat(items).map((i) => (
                <tr key={`${i.kind}-${i.id}`} className="border-b border-line">
                  <td className="py-3 pr-4">
                    {i.title}
                    <span className="block text-muted">
                      {[i.cityName, i.date && formatDate(i.date)].filter(Boolean).join(' · ')}
                    </span>
                  </td>
                  <td className="py-3 pr-4">{r.kind[i.kind]}</td>
                  <td className="py-3 pr-4">{p.adminStatus[i.adminStatus]}</td>
                  <td className="py-3 pr-4">{i.sourceName}</td>
                  <td className="py-3">
                    <div className="flex flex-wrap gap-2">
                      <Link to={`/admin/published/${i.kind}/${i.id}`} className={buttonClass}>
                        {p.edit}
                      </Link>
                      <button
                        type="button"
                        className={buttonClass}
                        disabled={i.adminStatus !== 'published' || expire.isPending}
                        onClick={() => act(expire, i)}
                      >
                        {p.expire}
                      </button>
                      <button
                        type="button"
                        className={buttonClass}
                        disabled={i.adminStatus === 'withdrawn' || withdraw.isPending}
                        onClick={() => act(withdraw, i)}
                      >
                        {p.withdraw}
                      </button>
                    </div>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </ListShell>
    </>
  )
}
