import { defineConfig, devices } from '@playwright/test';

const LOCAL_PORT = 4173;
const LOCAL_URL = `https://127.0.0.1:${LOCAL_PORT}`;

// Sem E2E_BASE_URL o suite sobe o build de produção do front localmente.
// Para rodar contra o ambiente publicado:
//   PowerShell: $env:E2E_BASE_URL='https://cerulean-syrniki-02dc26.netlify.app'; npm run test:e2e
const externalBaseUrl = process.env.E2E_BASE_URL?.trim();
const baseURL = externalBaseUrl || LOCAL_URL;

export default defineConfig({
  testDir: './e2e',
  outputDir: './test-results',
  timeout: 45_000,
  expect: { timeout: 10_000 },
  fullyParallel: true,
  forbidOnly: Boolean(process.env.CI),
  retries: process.env.CI ? 2 : 0,
  workers: process.env.CI ? 1 : undefined,
  reporter: [
    ['list'],
    ['html', { outputFolder: 'playwright-report', open: 'never' }],
    ['junit', { outputFile: 'reports/junit.xml' }],
  ],
  use: {
    baseURL,
    ignoreHTTPSErrors: !externalBaseUrl, // Apenas para o certificado temporário do preview local
    locale: 'pt-BR',
    timezoneId: 'America/Sao_Paulo',
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
    video: 'retain-on-failure',
  },
  projects: [
    {
      name: 'chromium',
      use: { ...devices['Desktop Chrome'] },
    },
  ],
  webServer: externalBaseUrl
    ? undefined
    : {
        command: `npm --prefix ../../src/frontend run preview -- --host 127.0.0.1 --port ${LOCAL_PORT} --strictPort`,
        url: LOCAL_URL,
        ignoreHTTPSErrors: true, // Verificação de disponibilidade do preview com certificado local
        reuseExistingServer: !process.env.CI,
        timeout: 120_000,
      },
});
