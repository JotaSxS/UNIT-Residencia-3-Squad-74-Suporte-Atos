import { defineConfig } from 'vitest/config';
import { loadEnv } from 'vite';
import react from '@vitejs/plugin-react';
import { existsSync, readFileSync } from 'node:fs';
import { homedir } from 'node:os';
import { join } from 'node:path';

export default defineConfig(({ command, mode }) => {
  const env = loadEnv(mode, process.cwd(), 'SUPPORTATOS_');
  const certificate = process.env.SUPPORTATOS_HTTPS_CERT || env.SUPPORTATOS_HTTPS_CERT
    || join(homedir(), '.aspnet', 'https', 'supportatos-frontend.pem');
  const privateKey = process.env.SUPPORTATOS_HTTPS_KEY || env.SUPPORTATOS_HTTPS_KEY
    || join(homedir(), '.aspnet', 'https', 'supportatos-frontend.key');
  const serveHttps = command === 'serve' && mode !== 'test';
  if (serveHttps && (!existsSync(certificate) || !existsSync(privateKey))) {
    throw new Error('Certificado HTTPS local ausente. Siga a preparação em README.md; não há fallback para HTTP.');
  }
  return {
    plugins: [react()],
    server: {
      host: 'localhost',
      port: 5173,
      strictPort: true,
      https: serveHttps ? { cert: readFileSync(certificate), key: readFileSync(privateKey) } : undefined,
      watch: {
        ignored: ['**/backend/**'],
      },
    },
    // Testes (npm test): Vitest + Testing Library em jsdom.
    test: {
      environment: 'jsdom',
      setupFiles: ['./src/test/setup.ts'],
    },
  };
});
