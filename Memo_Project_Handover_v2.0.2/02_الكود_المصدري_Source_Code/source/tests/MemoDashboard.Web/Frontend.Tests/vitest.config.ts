import { fileURLToPath, URL } from 'node:url'
import { defineConfig } from 'vitest/config'
import react from '@vitejs/plugin-react'

export default defineConfig({
  plugins: [react()],
  test: {
    root: fileURLToPath(new URL('.', import.meta.url)),
    environment: 'jsdom',
    globals: true,
    setupFiles: ['./setup.ts'],
    css: false,
    include: ['./**/*.test.{ts,tsx}'],
  },
  server: {
    fs: {
      allow: [fileURLToPath(new URL('../../../src/MemoDashboard.Web/Frontend/src', import.meta.url))],
    },
  },
})
