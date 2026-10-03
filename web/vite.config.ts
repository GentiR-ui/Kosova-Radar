/// <reference types="vitest/config" />
import tailwindcss from '@tailwindcss/vite'
import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'

// The API runs on :5080 in development; the browser only talks to the Vite origin (same-origin cookies).
export default defineConfig({
  plugins: [react(), tailwindcss()],
  server: {
    port: 5173,
    proxy: { '/api': 'http://localhost:5080', '/health': 'http://localhost:5080' },
  },
  test: {
    environment: 'jsdom',
    // The default "forks" pool times out starting workers on Windows; threads start reliably.
    pool: 'threads',
    setupFiles: ['./src/test/setup.ts'],
    include: ['src/**/*.test.{ts,tsx}'],
  },
})
