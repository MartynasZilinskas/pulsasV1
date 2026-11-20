import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'

export default defineConfig({
  plugins: [
    react(),
    {
      name: 'pulsas-readiness',
      configureServer(server) {
        server.middlewares.use('/health', (_request, response) => {
          response.setHeader('Content-Type', 'text/plain; charset=utf-8')
          response.end('OK')
        })
      },
    },
  ],
  server: {
    host: '127.0.0.1',
    port: Number(process.env.PORT ?? 5173),
    strictPort: true,
  },
})
