import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

export default defineConfig({
  plugins: [react()],
  build: {
    outDir: '../Backend/wwwroot',
    emptyOutDir: true,
  },
  server: {
    // Dev-only server: bind loopback so the known vite dev-server file-read
    // CVEs (fixed in 5.4.12+) are not exposed to the LAN. Use
    // `vite --host <lan-ip>` explicitly when LAN preview is really needed.
    host: '127.0.0.1',
    port: 5173,
  },
})
