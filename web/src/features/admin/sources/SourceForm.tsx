import { useState, type FormEvent } from 'react'
import { ApiError } from '../../../lib/api'
import type { SourceDetailDto, SourceRequest, SourceType } from './api'
import { s } from './strings'

const inputClass = 'w-full rounded-md border border-line bg-surface px-3 py-2.5'

interface Props {
  initial: SourceDetailDto | null
  pending: boolean
  error: unknown
  onSubmit: (body: SourceRequest) => void
  onCancel: () => void
}

/** Lower-cased field names named by a 400 problem-details response. */
function fieldErrors(error: unknown): Set<string> {
  const keys = new Set<string>()
  if (error instanceof ApiError && error.status === 400) {
    for (const k of Object.keys(error.problem.errors ?? {})) keys.add(k.toLowerCase())
  }
  return keys
}

export default function SourceForm({ initial, pending, error, onSubmit, onCancel }: Props) {
  const [name, setName] = useState(initial?.name ?? '')
  const [type, setType] = useState<SourceType>(initial?.type ?? 'jsonFeed')
  const [baseUrl, setBaseUrl] = useState(initial?.baseUrl ?? '')
  const [payload, setPayload] = useState(initial?.fixturePayload ?? '')
  const [kind, setKind] = useState<'event' | 'deal'>(initial?.defaultKind ?? 'event')
  const [interval, setInterval] = useState(String(initial?.pollIntervalMinutes ?? 60))
  const [isActive, setIsActive] = useState(initial?.isActive ?? false)
  const [isUnreliable, setIsUnreliable] = useState(initial?.isUnreliable ?? false)

  const bad = fieldErrors(error)
  const invalid = (field: string) => (bad.has(field) ? true : undefined)
  const message = !error ? null : bad.size > 0 ? s.invalidField : s.saveFailed

  const submit = (e: FormEvent) => {
    e.preventDefault()
    onSubmit({
      name: name.trim(),
      type,
      baseUrl: type === 'fixture' ? null : baseUrl.trim() || null,
      fixturePayload: type === 'fixture' ? payload : null,
      defaultKind: kind,
      pollIntervalMinutes: Number(interval),
      isActive,
      isUnreliable,
    })
  }

  return (
    <form
      onSubmit={submit}
      aria-label={initial ? s.formEdit : s.formAdd}
      className="mb-10 grid gap-5 rounded-lg border border-line p-5 sm:grid-cols-2"
    >
      <h2 className="text-xl sm:col-span-2">{initial ? s.formEdit : s.formAdd}</h2>
      <div className="space-y-1.5">
        <label htmlFor="src-name" className="block text-sm font-medium">
          {s.fields.name}
        </label>
        <input
          id="src-name"
          required
          maxLength={120}
          value={name}
          aria-invalid={invalid('name')}
          onChange={(e) => setName(e.target.value)}
          className={inputClass}
        />
      </div>
      <div className="space-y-1.5">
        <label htmlFor="src-type" className="block text-sm font-medium">
          {s.fields.type}
        </label>
        <select
          id="src-type"
          value={type}
          aria-invalid={invalid('type')}
          onChange={(e) => setType(e.target.value as SourceType)}
          className={inputClass}
        >
          {(Object.keys(s.types) as SourceType[]).map((k) => (
            <option key={k} value={k}>
              {s.types[k]}
            </option>
          ))}
        </select>
      </div>
      {type === 'fixture' ? (
        <div className="space-y-1.5 sm:col-span-2">
          <label htmlFor="src-payload" className="block text-sm font-medium">
            {s.fields.fixturePayload}
          </label>
          <textarea
            id="src-payload"
            rows={6}
            maxLength={1_000_000}
            value={payload}
            aria-invalid={invalid('fixturepayload')}
            onChange={(e) => setPayload(e.target.value)}
            className={`${inputClass} font-mono text-sm`}
          />
        </div>
      ) : (
        <div className="space-y-1.5 sm:col-span-2">
          <label htmlFor="src-url" className="block text-sm font-medium">
            {s.fields.baseUrl}
          </label>
          <input
            id="src-url"
            type="url"
            required
            maxLength={2000}
            value={baseUrl}
            aria-invalid={invalid('baseurl')}
            onChange={(e) => setBaseUrl(e.target.value)}
            className={inputClass}
          />
        </div>
      )}
      <div className="space-y-1.5">
        <label htmlFor="src-kind" className="block text-sm font-medium">
          {s.fields.defaultKind}
        </label>
        <select
          id="src-kind"
          value={kind}
          onChange={(e) => setKind(e.target.value as 'event' | 'deal')}
          className={inputClass}
        >
          <option value="event">{s.kinds.event}</option>
          <option value="deal">{s.kinds.deal}</option>
        </select>
      </div>
      <div className="space-y-1.5">
        <label htmlFor="src-interval" className="block text-sm font-medium">
          {s.fields.interval}
        </label>
        <input
          id="src-interval"
          type="number"
          required
          min={1}
          max={1440}
          value={interval}
          aria-invalid={invalid('pollintervalminutes')}
          onChange={(e) => setInterval(e.target.value)}
          className={inputClass}
        />
      </div>
      <label className="flex items-center gap-2 text-sm">
        <input type="checkbox" checked={isActive} onChange={(e) => setIsActive(e.target.checked)} />
        {s.fields.active}
      </label>
      <label className="flex items-center gap-2 text-sm">
        <input
          type="checkbox"
          checked={isUnreliable}
          onChange={(e) => setIsUnreliable(e.target.checked)}
        />
        {s.fields.unreliable}
      </label>
      {message && (
        <p role="alert" className="text-sm text-danger sm:col-span-2">
          {message}
        </p>
      )}
      <div className="flex gap-3 sm:col-span-2">
        <button
          type="submit"
          disabled={pending}
          className="rounded-md bg-accent px-4 py-2.5 font-medium text-accent-ink transition-opacity hover:opacity-90 disabled:opacity-60"
        >
          {s.save}
        </button>
        <button
          type="button"
          onClick={onCancel}
          className="rounded-md border border-line px-4 py-2.5 hover:bg-surface"
        >
          {s.cancel}
        </button>
      </div>
    </form>
  )
}
