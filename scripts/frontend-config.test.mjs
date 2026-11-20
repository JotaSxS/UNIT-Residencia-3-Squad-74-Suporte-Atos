import { test } from 'node:test';
import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';

const script = fileURLToPath(new URL('./frontend-config.mjs', import.meta.url));
test('resolves explicit certificate overrides with spaces without writing files', () => {
  const result = spawnSync(process.execPath, [script], { encoding: 'utf8', env: {
    ...process.env, VITE_API_URL: 'https://localhost:7099',
    SUPPORTATOS_HTTPS_CERT: 'C:/synthetic path/cert.pem', SUPPORTATOS_HTTPS_KEY: 'C:/synthetic path/cert.key',
  } });
  assert.equal(result.status, 0);
  const config = JSON.parse(result.stdout);
  assert.equal(config.custom, true);
  assert.match(config.cert, /synthetic path/);
  assert.match(config.key, /cert\.key$/);
});
test('rejects another API URL without exposing credentials in diagnostics', () => {
  const result = spawnSync(process.execPath, [script], { encoding: 'utf8', env: {
    ...process.env, VITE_API_URL: 'https://secret-user:secret-password@other.example',
  } });
  assert.equal(result.status, 1);
  assert.doesNotMatch(result.stdout + result.stderr, /secret-user|secret-password|other\.example/);
});
