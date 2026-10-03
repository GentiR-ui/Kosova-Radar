import { useState, type FormEvent } from 'react'
import { Navigate } from 'react-router'
import { ApiError } from '../../lib/api'
import { t } from '../../lib/strings'
import { useLogin, useMe } from './api'

const s = t.admin.login

export default function LoginPage() {
  const me = useMe()
  const login = useLogin()
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')

  if (me.data) return <Navigate to="/admin" replace />

  const onSubmit = (e: FormEvent) => {
    e.preventDefault()
    login.mutate({ email, password })
  }

  const error =
    login.error instanceof ApiError
      ? login.error.status === 429
        ? s.throttled
        : login.error.status === 401 || login.error.status === 400
          ? s.invalid
          : s.failed
      : login.error
        ? s.failed
        : null

  return (
    <main className="mx-auto flex min-h-dvh max-w-sm flex-col justify-center px-6 py-16">
      <p className="mb-10 text-sm tracking-[0.2em] text-muted uppercase">{t.brand}</p>
      <h1 className="mb-8 text-3xl">{s.title}</h1>
      <form onSubmit={onSubmit} className="space-y-5" noValidate>
        <div className="space-y-1.5">
          <label htmlFor="email" className="block text-sm font-medium">
            {s.email}
          </label>
          <input
            id="email"
            type="email"
            autoComplete="username"
            aria-invalid={error ? true : undefined}
            aria-describedby={error ? 'login-error' : undefined}
            required
            value={email}
            onChange={(e) => setEmail(e.target.value)}
            className="w-full rounded-md border border-line bg-surface px-3 py-2.5"
          />
        </div>
        <div className="space-y-1.5">
          <label htmlFor="password" className="block text-sm font-medium">
            {s.password}
          </label>
          <input
            id="password"
            type="password"
            autoComplete="current-password"
            aria-invalid={error ? true : undefined}
            aria-describedby={error ? 'login-error' : undefined}
            required
            value={password}
            onChange={(e) => setPassword(e.target.value)}
            className="w-full rounded-md border border-line bg-surface px-3 py-2.5"
          />
        </div>
        {error && (
          <p id="login-error" role="alert" className="text-sm text-danger">
            {error}
          </p>
        )}
        <button
          type="submit"
          disabled={login.isPending}
          className="w-full rounded-md bg-accent px-4 py-2.5 font-medium text-accent-ink transition-opacity hover:opacity-90 disabled:opacity-60"
        >
          {s.submit}
        </button>
      </form>
    </main>
  )
}
