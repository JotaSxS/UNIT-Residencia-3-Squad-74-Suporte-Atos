import { defineConfig } from 'vitest/config';
import react from '@vitejs/plugin-react';

export default defineConfig({
  // Prefer the current TypeScript components over their legacy JS copies.
  resolve: { extensions: ['.mjs', '.mts', '.ts', '.tsx', '.js', '.jsx', '.json'] },
  plugins: [react()],
  server: {
    port: 5173,
    watch: {
      ignored: ['**/backend/**'],
    },
  },
  // Testes (npm test): Vitest + Testing Library em jsdom.
  test: {
    environment: 'jsdom',
    setupFiles: ['./src/test/setup.ts'],
  },
});
