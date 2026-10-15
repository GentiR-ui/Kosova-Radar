import type { ReactNode } from 'react'
import { Field, selectClass } from '../../public/Field'
import { useCategories, useCities } from '../../public/api'
import { fromPristinaInput, toPristinaInput } from '../../public/format'
import type { CandidateFields, FieldsRequest, Kind } from './api'
import { r } from './strings'

const inputClass = 'w-full rounded-sm border border-line bg-surface px-3 py-2.5'
const text = (v: string | number | null) => (v === null ? '' : String(v))

export type Values = Record<string, string>

export function FieldsForm({
  kind,
  suggestedCategoryName,
  values,
  setValues,
  errors,
  editable,
}: {
  kind: Kind
  suggestedCategoryName?: string
  values: Values
  setValues: (v: Values) => void
  errors: Record<string, string>
  editable: boolean
}) {
  const deal = kind === 'deal'
  const cities = useCities()
  const categories = useCategories(kind)
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
    // `editable={false}` disables every input, so read-only items cannot be changed.
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
          <option value="">{suggestedCategoryName ?? r.fields.none}</option>
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
          {field('startDate', r.fields.startDate, 'date')}
          {field('endsAt', r.fields.endsAt, 'datetime-local')}
        </>
      )}
      {field('priceText', r.fields.priceText)}
      {!deal && field('priceAmount', r.fields.priceAmount, 'number', money)}
      {field('currency', r.fields.currency, 'text', { maxLength: 3 })}
    </fieldset>
  )
}

export function initial(f: CandidateFields): Record<string, string> {
  return {
    title: text(f.title),
    description: text(f.description),
    url: text(f.url),
    citySlug: text(f.citySlug),
    venueName: text(f.venueName),
    categorySlug: text(f.categorySlug),
    merchantName: text(f.merchantName),
    startsAt: toPristinaInput(f.startsAt),
    startDate: text(f.startDate ?? null),
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
export function toRequest(f: CandidateFields, v: Values): FieldsRequest {
  const { cityText: _c, categoryText: _k, discountPercent: _d, ...rest } = f
  const base = initial(f)
  const out: FieldsRequest = { ...rest }
  const set = <K extends keyof FieldsRequest>(key: K, convert: (x: string) => FieldsRequest[K]) => {
    const value = v[key] ?? ''
    if (value !== base[key]) out[key] = convert(value)
  }
  for (const k of [
    'title',
    'description',
    'url',
    'citySlug',
    'venueName',
    'categorySlug',
    'merchantName',
    'priceText',
    'currency',
    'startDate',
  ] as const)
    set(k, trimOrNull)
  for (const k of ['startsAt', 'endsAt', 'validFrom', 'validUntil'] as const)
    set(k, fromPristinaInput)
  for (const k of ['priceAmount', 'oldPrice', 'newPrice'] as const) set(k, num)
  return out
}
