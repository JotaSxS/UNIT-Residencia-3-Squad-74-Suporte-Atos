import React from 'react';
import { beforeAll, beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { ThemeProvider } from '@mui/material/styles';
import { buildTheme } from '../theme';
import i18n from '../i18n';
import { login } from '../auth';
import { notifyApiError } from '../notify';
import LoginScreen from './Login';

vi.mock('../auth', () => ({ login: vi.fn() }));
vi.mock('../notify', () => ({ notifyApiError: vi.fn() }));
beforeAll(async () => { await i18n.changeLanguage('pt'); });
beforeEach(() => vi.clearAllMocks());
function setup() {
  const onLogin = vi.fn();
  render(<ThemeProvider theme={buildTheme('light')}><LoginScreen onLogin={onLogin} themeMode="light" setThemeMode={vi.fn()} /></ThemeProvider>);
  return onLogin;
}
async function submit() {
  const user = userEvent.setup();
  await user.type(screen.getByLabelText(/e-mail corporativo/i), 'admin@qa.invalid');
  await user.type(screen.getByLabelText(/^senha/i), 'senha de teste');
  await user.click(screen.getByRole('button', { name: 'Entrar' }));
}
describe('LoginScreen real', () => {
  it('envia a senha e repassa a identidade recebida, inclusive papel não Admin', async () => {
    const session = { usuarioId: 9, role: 'agente' as const, nome: 'Pessoa do servidor', email: 'admin@qa.invalid', empresa: null };
    vi.mocked(login).mockResolvedValue(session);
    const onLogin = setup();
    await submit();
    await waitFor(() => expect(onLogin).toHaveBeenCalledWith(session));
    expect(login).toHaveBeenCalledWith('admin@qa.invalid', 'senha de teste');
  });
  it('não entra no portal quando o login falha e permite tentar novamente', async () => {
    const error = new Error('Credenciais inválidas');
    vi.mocked(login).mockRejectedValue(error);
    const onLogin = setup();
    await submit();
    await waitFor(() => expect(notifyApiError).toHaveBeenCalledWith(error, expect.any(String)));
    expect(onLogin).not.toHaveBeenCalled();
    expect(screen.getByRole('button', { name: 'Entrar' })).toBeEnabled();
  });
});
