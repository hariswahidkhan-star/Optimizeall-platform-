/// <reference types="vitest/config" />
import { fileURLToPath, URL } from 'node:url';
import react from '@vitejs/plugin-react';
import { defineConfig, loadEnv } from 'vite';
import { devProxy, redirectGate } from './src/app/devProxy';

export default defineConfig(({ mode }) => {
  const env = loadEnv(mode, process.cwd(), '');
  const apiTarget = env.VITE_API_PROXY_TARGET || 'http://127.0.0.1:5080';
  const proxy = devProxy(apiTarget);

  return {
    // redirectGate: moved public addresses get a real 301 before the app shell, like nginx (Website → Redirects).
    plugins: [react(), redirectGate(apiTarget, proxy)],
    resolve: {
      alias: { '@': fileURLToPath(new URL('./src', import.meta.url)) },
    },
    server: { port: 5173, strictPort: true, proxy },
    preview: { port: 5173, strictPort: true, proxy },
    build: {
      target: 'es2022',
      // No source maps in the production bundle: nginx serves dist/ publicly and there is no error-reporting
      // pipeline that would need them (nginx also answers 404 for *.map; CI fails if the build emits any).
      sourcemap: false,
      rollupOptions: {
        output: {
          manualChunks: {
            react: ['react', 'react-dom', 'react-router-dom'],
            query: ['@tanstack/react-query'],
          },
        },
      },
    },
    test: {
      globals: true,
      environment: 'jsdom',
      setupFiles: ['./src/test/setup.ts'],
      include: ['src/**/*.test.{ts,tsx}'],
      css: false,
      // axe over full pages (e.g. the ~650 country/time-zone options on Register) needs more than the 5s default.
      testTimeout: 30_000,
      restoreMocks: true,
      unstubGlobals: true,
    },
  };
});
