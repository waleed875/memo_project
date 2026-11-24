import { fileURLToPath, URL } from 'node:url'
import { defineConfig } from 'vitest/config'
import react from '@vitejs/plugin-react'

const frontendRoot = fileURLToPath(new URL('.', import.meta.url))
const testRoot = fileURLToPath(new URL('../../../tests/MemoDashboard.Web/Frontend.Tests/', import.meta.url))

export default defineConfig({
  plugins: [react()],
  resolve: {
    alias: {
      react: fileURLToPath(new URL('./node_modules/react', import.meta.url)),
      '@testing-library/react': fileURLToPath(new URL('./node_modules/@testing-library/react', import.meta.url)),
      'react-chartjs-2': fileURLToPath(new URL('./node_modules/react-chartjs-2', import.meta.url)),
      'chart.js': fileURLToPath(new URL('./node_modules/chart.js', import.meta.url)),
    },
  },
  test: {
    root: testRoot,
    environment: 'jsdom',
    globals: true,
    setupFiles: [fileURLToPath(new URL('./setupTests.ts', import.meta.url))],
    css: false,
    include: ['./**/*.test.{ts,tsx}'],
  },
  server: { fs: { allow: [frontendRoot, testRoot] } },
})