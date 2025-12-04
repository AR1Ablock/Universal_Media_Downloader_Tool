import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'
import path from 'path'

export default defineConfig(({ mode }) => {
  const isProduction = mode === 'production'

  return {
    plugins: [vue()],
    resolve: {
      alias: {
        '@': path.resolve(__dirname, 'src'),
      },
    },
    build: {
      sourcemap: false, // Disable source maps in production
      minify: 'esbuild', // Use Terser for smaller JS
      cssCodeSplit: true, // Separate CSS per chunk
      emptyOutDir: true, // Clean output dir before build
      rollupOptions: {
        output: {
          manualChunks: {
            vendor: ['vue'], // Split vendor code
          },
        },
      },
      target: 'es2015', // Better browser compatibility
    },
    worker: {
      format: 'es', // Ensure ES module format for workers
    },
    // Dev server config — ignored in production
    server: {
      hmr: true,
      watch: {
        ignored: ['**/node_modules/**', '**/dist/**'],
      },
      port: 5173,
      strictPort: true,
      cors: true,
      host: '0.0.0.0',
      allowedHosts: ['.ngrok-free.app'],
    },
  }
})