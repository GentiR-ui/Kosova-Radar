import { useState } from 'react'
import { ApiError } from '../../../lib/api'
import { t } from '../../../lib/strings'
import { formatDateTime } from '../../public/format'
import {
  useFetchSource,
  useSaveSource,
  useSourceDetail,
  useSources,
  type RunDto,
  type SourceRequest,
} from './api'
import SourceForm from './SourceForm'
import { s } from './strings'

/** `null` = form closed, `'new'` = add form, otherwise the id being edited. */
type FormState = string | null

function runMessage(run: RunDto): string {
  return run.status === 'failed'
    ? s.fetchFailed(run.errorCode ?? 'Unknown')
    : s.fetchOk(run.created, run.updated, run.unchanged, run.failed)
}

export default function SourcesPage() {
  const sources = useSources()
  const save = useSaveSource()
  const fetchNow = useFetchSource()
  const [form, setForm] = useState<FormState>(null)
  const [notice, setNotice] = useState<string | null>(null)
  const [fetchingId, setFetchingId] = useState<string | null>(null)
  const editingId = form !== null && form !== 'new' ? form : null
  const detail = useSourceDetail(editingId)

  const closeForm = () => {
    setForm(null)
    save.reset()
  }

  const open = (next: string) => {
    save.reset()
    setForm(next)
  }

  const onSubmit = (body: SourceRequest) => {
    setNotice(null)
    save.mutate(
      { id: editingId, body },
      {
        onSuccess: () => {
          closeForm()
          setNotice(s.saved)
        },
      },
    )
  }

  const onFetch = (id: string) => {
    setNotice(null)
    setFetchingId(id)
    fetchNow.mutate(id, {
      onSuccess: (run) => setNotice(runMessage(run)),
      onError: (e) =>
        setNotice(e instanceof ApiError && e.status === 409 ? s.fetchBusy : s.fetchError),
      onSettled: () => setFetchingId(null),
    })
  }

  return (
    <>
      <div className="mb-8 flex flex-wrap items-center justify-between gap-4">
        <h1 className="text-3xl">{s.title}</h1>
        <button
          type="button"
          onClick={() => open('new')}
          className="rounded-md bg-accent px-4 py-2.5 font-medium text-accent-ink transition-opacity hover:opacity-90"
        >
          {s.add}
        </button>
      </div>

      {form === 'new' && (
        <SourceForm
          key="new"
          initial={null}
          pending={save.isPending}
          error={save.error}
          onSubmit={onSubmit}
          onCancel={closeForm}
        />
      )}
      {editingId && detail.isPending && (
        <p role="status" className="mb-6">
          {t.loading}
        </p>
      )}
      {editingId && detail.isError && (
        <p role="alert" className="mb-6 text-sm text-danger">
          {s.loadFailed}
        </p>
      )}
      {editingId && detail.data && (
        <SourceForm
          key={detail.data.id}
          initial={detail.data}
          pending={save.isPending}
          error={save.error}
          onSubmit={onSubmit}
          onCancel={closeForm}
        />
      )}

      <div role="status" className="mb-4 min-h-5 text-sm">
        {notice}
        {sources.isPending && t.loading}
      </div>
      {sources.isError && (
        <div role="alert" className="mb-6 flex items-center gap-3 text-sm">
          <span>{t.error}</span>
          <button
            type="button"
            onClick={() => void sources.refetch()}
            className="rounded-md border border-line px-3 py-1"
          >
            {t.retry}
          </button>
        </div>
      )}
      {sources.data?.length === 0 && <p className="text-muted">{s.empty}</p>}
      {!!sources.data?.length && (
        <div className="overflow-x-auto rounded-lg border border-line">
          <table aria-label={s.list} className="w-full min-w-[44rem] text-left text-sm">
            <thead className="border-b border-line bg-surface text-muted">
              <tr>
                <th scope="col" className="px-4 py-3 font-medium">
                  {s.columns.name}
                </th>
                <th scope="col" className="px-4 py-3 font-medium">
                  {s.columns.health}
                </th>
                <th scope="col" className="px-4 py-3 font-medium">
                  {s.columns.lastSuccess}
                </th>
                <th scope="col" className="px-4 py-3 font-medium">
                  {s.columns.lastError}
                </th>
                <th scope="col" className="px-4 py-3">
                  <span className="sr-only">{s.columns.actions}</span>
                </th>
              </tr>
            </thead>
            <tbody>
              {sources.data.map((src) => (
                <tr key={src.id} className="border-b border-line align-top last:border-b-0">
                  <th scope="row" className="px-4 py-3 font-normal">
                    {src.name}
                  </th>
                  <td className="px-4 py-3">{s.health[src.health]}</td>
                  <td className="px-4 py-3">
                    {src.lastSuccessAt ? formatDateTime(src.lastSuccessAt) : s.never}
                  </td>
                  <td className="px-4 py-3 break-words">{src.lastError ?? s.never}</td>
                  <td className="px-4 py-3">
                    <div className="flex flex-wrap justify-end gap-2">
                      <button
                        type="button"
                        onClick={() => onFetch(src.id)}
                        disabled={fetchingId !== null}
                        aria-label={`${s.fetchNow}: ${src.name}`}
                        className="rounded-md border border-line px-3 py-1.5 hover:bg-surface disabled:opacity-60"
                      >
                        {s.fetchNow}
                      </button>
                      <button
                        type="button"
                        onClick={() => open(src.id)}
                        aria-label={`${s.edit}: ${src.name}`}
                        className="rounded-md border border-line px-3 py-1.5 hover:bg-surface"
                      >
                        {s.edit}
                      </button>
                    </div>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </>
  )
}
