import { useState, type FormEvent } from 'react'
import { ApiError } from '../../../lib/api'
import { t } from '../../../lib/strings'
import { useMe, type Role } from '../../auth/api'
import { useCreateUser, useDeactivateUser, useUsers } from './api'

const s = t.admin.users

const inputClass = 'w-full rounded-md border border-line bg-surface px-3 py-2.5'

export default function UsersPage() {
  const users = useUsers()
  const me = useMe()
  const create = useCreateUser()
  const deactivate = useDeactivateUser()
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [role, setRole] = useState<Role>('reviewer')
  const [notice, setNotice] = useState<string | null>(null)

  const onCreate = (e: FormEvent) => {
    e.preventDefault()
    setNotice(null)
    create.mutate(
      { email, password, role },
      {
        onSuccess: () => {
          setEmail('')
          setPassword('')
          setNotice(s.created)
        },
      },
    )
  }

  const onDeactivate = (id: string) => {
    setNotice(null)
    deactivate.mutate(id, { onSuccess: () => setNotice(s.deactivated) })
  }

  const createError =
    create.error instanceof ApiError
      ? create.error.status === 409
        ? s.emailTaken
        : create.error.status === 400
          ? s.invalid
          : t.error
      : create.error
        ? t.error
        : null

  return (
    <>
      <h1 className="mb-8 text-3xl">{s.title}</h1>

      <form
        onSubmit={onCreate}
        className="mb-10 grid gap-5 sm:grid-cols-2 lg:grid-cols-4"
        noValidate
      >
        <div className="space-y-1.5">
          <label htmlFor="user-email" className="block text-sm font-medium">
            {s.email}
          </label>
          <input
            id="user-email"
            type="email"
            autoComplete="off"
            required
            maxLength={256}
            aria-invalid={createError ? true : undefined}
            aria-describedby={createError ? 'user-create-error' : undefined}
            value={email}
            onChange={(e) => setEmail(e.target.value)}
            className={inputClass}
          />
        </div>
        <div className="space-y-1.5">
          <label htmlFor="user-password" className="block text-sm font-medium">
            {s.password}
          </label>
          <input
            id="user-password"
            type="password"
            autoComplete="new-password"
            required
            minLength={12}
            maxLength={128}
            aria-invalid={createError ? true : undefined}
            aria-describedby={
              createError ? 'user-create-error user-password-hint' : 'user-password-hint'
            }
            value={password}
            onChange={(e) => setPassword(e.target.value)}
            className={inputClass}
          />
          <p id="user-password-hint" className="text-xs text-muted">
            {s.passwordHint}
          </p>
        </div>
        <div className="space-y-1.5">
          <label htmlFor="user-role" className="block text-sm font-medium">
            {s.role}
          </label>
          <select
            id="user-role"
            value={role}
            onChange={(e) => setRole(e.target.value as Role)}
            className={inputClass}
          >
            <option value="admin">{s.roles.admin}</option>
            <option value="reviewer">{s.roles.reviewer}</option>
          </select>
        </div>
        <div className="flex items-end">
          <button
            type="submit"
            disabled={create.isPending}
            className="w-full rounded-md bg-accent px-4 py-2.5 font-medium text-accent-ink transition-opacity hover:opacity-90 disabled:opacity-60"
          >
            {s.create}
          </button>
        </div>
        {createError && (
          <p
            id="user-create-error"
            role="alert"
            className="text-sm text-danger sm:col-span-2 lg:col-span-4"
          >
            {createError}
          </p>
        )}
      </form>

      <div role="status" className="mb-4 min-h-5 text-sm">
        {notice}
        {users.isPending && t.loading}
      </div>
      {deactivate.isError && (
        <p role="alert" className="mb-4 text-sm text-danger">
          {s.deactivateFailed}
        </p>
      )}
      {users.isError && (
        <div role="alert" className="mb-6 flex items-center gap-3 text-sm">
          <span>{t.error}</span>
          <button
            type="button"
            onClick={() => void users.refetch()}
            className="rounded-md border border-line px-3 py-1"
          >
            {t.retry}
          </button>
        </div>
      )}
      {users.data?.length === 0 && <p className="text-muted">{s.empty}</p>}
      {!!users.data?.length && (
        <div className="overflow-x-auto rounded-lg border border-line">
          <table aria-label={s.list} className="w-full min-w-[34rem] text-left text-sm">
            <thead className="border-b border-line bg-surface text-muted">
              <tr>
                <th scope="col" className="px-4 py-3 font-medium">
                  {s.email}
                </th>
                <th scope="col" className="px-4 py-3 font-medium">
                  {s.role}
                </th>
                <th scope="col" className="px-4 py-3 font-medium">
                  {s.status}
                </th>
                <th scope="col" className="px-4 py-3">
                  <span className="sr-only">{s.actions}</span>
                </th>
              </tr>
            </thead>
            <tbody>
              {users.data.map((u) => (
                <tr key={u.id} className="border-b border-line last:border-b-0">
                  <th scope="row" className="px-4 py-3 font-normal break-all">
                    {u.email}
                    {u.id === me.data?.userId && <span className="ml-2 text-muted">{s.you}</span>}
                  </th>
                  <td className="px-4 py-3">{s.roles[u.role]}</td>
                  <td className="px-4 py-3">{u.isActive ? s.active : s.inactive}</td>
                  <td className="px-4 py-3 text-right">
                    {u.isActive && me.data && u.id !== me.data.userId && (
                      <button
                        type="button"
                        onClick={() => onDeactivate(u.id)}
                        disabled={deactivate.isPending}
                        aria-label={`${s.deactivate} ${u.email}`}
                        className="rounded-md border border-line px-3 py-1.5 hover:bg-surface disabled:opacity-60"
                      >
                        {s.deactivate}
                      </button>
                    )}
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
