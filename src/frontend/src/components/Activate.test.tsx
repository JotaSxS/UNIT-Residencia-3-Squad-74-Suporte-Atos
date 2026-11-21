// Página pública de ativação: token só em memória, validação da senha e envio ao endpoint real.
import React from 'react';
import { beforeAll, beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { ThemeProvider } from '@mui/material/styles';
import { BrowserRouter, MemoryRouter, Route, Routes, useLocation } from 'react-router-dom';
import i18n from '../i18n';
import { buildTheme } from '../theme';
import { ApiError } from '../http';
import { ativarFuncionario } from '../auth';
import Activate from './Activate';

vi.mock('../auth', () => ({ ativarFuncionario: vi.fn() }));

function LocationProbe() {
  const location = useLocation();
  return <div data-testid="location">{`${location.pathname}${location.search}`}</div>;
}

function LoginRoute() {
  const location = useLocation();
  const success = Boolean((location.state as { activationSuccess?: boolean } | null)?.activationSuccess);
  return <div>{success ? 'LOGIN ATIVADO' : 'LOGIN'}</div>;
}

function renderActivation(initialEntry = '/ativar?token=token-valido') {
  render(
    <ThemeProvider theme={buildTheme('light')}>
      <MemoryRouter initialEntries={[initialEntry]}>
        <Routes>
          <Route path="/ativar" element={<><Activate /><LocationProbe /></>} />
          <Route path="/login" element={<LoginRoute />} />
        </Routes>
      </MemoryRouter>
    </ThemeProvider>,
  );
}

async function preencherSenhas(senha: string, confirmacao: string) {
  fireEvent.change(screen.getByLabelText(/^nova senha/i), { target: { value: senha } });
  fireEvent.change(screen.getByLabelText(/confirmar senha/i), { target: { value: confirmacao } });
}

describe('Activate — ativação pública', () => {
  beforeAll(async () => { await i18n.changeLanguage('pt'); });
  beforeEach(() => { vi.resetAllMocks(); });

  it('captura o token e o remove imediatamente da URL, sem exibi-lo', async () => {
    renderActivation('/ativar?token=segredo-na-url');

    expect(await screen.findByRole('heading', { name: 'Defina sua primeira senha' })).toBeInTheDocument();
    await waitFor(() => expect(screen.getByTestId('location')).toHaveTextContent(/^\/ativar$/));
    expect(screen.getByTestId('location')).not.toHaveTextContent('segredo-na-url');
    expect(document.body.textContent).not.toContain('segredo-na-url');
  });

  it('trata token ausente sem mostrar o formulário', async () => {
    renderActivation('/ativar');

    expect(await screen.findByText(/não contém um token válido/i)).toBeInTheDocument();
    expect(screen.queryByLabelText(/^nova senha/i)).not.toBeInTheDocument();
    expect(screen.getAllByRole('button', { name: 'Ir para o login' }).length).toBeGreaterThan(0);
  });

  it('valida obrigatoriedade, tamanho mínimo e confirmação da senha', async () => {
    const u = userEvent.setup();
    renderActivation();
    const senha = screen.getByLabelText(/^nova senha/i);
    const confirmacao = screen.getByLabelText(/confirmar senha/i);

    await u.click(screen.getByRole('button', { name: 'Ativar acesso' }));
    await waitFor(() => expect(senha).toHaveAccessibleDescription('Campo obrigatório.'));
    expect(confirmacao).toHaveAccessibleDescription('Campo obrigatório.');

    await preencherSenhas('curta', 'curta');
    await u.click(screen.getByRole('button', { name: 'Ativar acesso' }));
    await waitFor(() => expect(senha).toHaveAccessibleDescription('A senha deve ter pelo menos 15 caracteres.'));

    await preencherSenhas('senha-segura-123', 'outra-senha-12345');
    await u.click(screen.getByRole('button', { name: 'Ativar acesso' }));
    await waitFor(() => expect(confirmacao).toHaveAccessibleDescription('As senhas não coincidem.'));
    expect(ativarFuncionario).not.toHaveBeenCalled();
  });

  it('ativa com sucesso, limpa os dados e encaminha ao login com mensagem', async () => {
    vi.mocked(ativarFuncionario).mockResolvedValue(undefined);
    const u = userEvent.setup();
    renderActivation();
    const storageAntes = [JSON.stringify(localStorage), JSON.stringify(sessionStorage)];

    await preencherSenhas('senha-segura-123', 'senha-segura-123');
    await u.click(screen.getByRole('button', { name: 'Ativar acesso' }));

    await waitFor(() => expect(ativarFuncionario).toHaveBeenCalledWith({ token: 'token-valido', senha: 'senha-segura-123' }));
    expect(await screen.findByText('LOGIN ATIVADO')).toBeInTheDocument();
    expect([JSON.stringify(localStorage), JSON.stringify(sessionStorage)]).toEqual(storageAntes);
  });

  it.each([
    [400, 'Este convite é inválido, expirou ou já foi utilizado. Peça um novo convite ao administrador.'],
    [500, 'Não foi possível concluir a solicitação. Tente novamente.'],
  ])('trata falha HTTP %s sem expor detalhes do servidor', async (status, mensagem) => {
    vi.mocked(ativarFuncionario).mockRejectedValue(new ApiError('detalhe sensível do servidor', status));
    const u = userEvent.setup();
    renderActivation();

    await preencherSenhas('senha-segura-123', 'senha-segura-123');
    await u.click(screen.getByRole('button', { name: 'Ativar acesso' }));

    expect(await screen.findByRole('alert')).toHaveTextContent(mensagem);
    expect(screen.queryByText('detalhe sensível do servidor')).not.toBeInTheDocument();
    expect(screen.getByTestId('location')).toHaveTextContent('/ativar');
  });
});

it.each(['?token=segredo-real', '#token=segredo-real'])('limpa historico real %s sem perder token em memoria (StrictMode)', async (suffix) => {
  window.history.replaceState(null, '', `/ativar${suffix}`);
  const tamanhoAntes = window.history.length;
  const u = userEvent.setup();
  vi.mocked(ativarFuncionario).mockResolvedValue(undefined);
  const view = render(<React.StrictMode><ThemeProvider theme={buildTheme('light')}>
    <BrowserRouter><Routes>
      <Route path="/ativar" element={<><Activate /><LocationProbe /></>} />
      <Route path="/login" element={<LoginRoute />} />
    </Routes></BrowserRouter>
  </ThemeProvider></React.StrictMode>);
  try {
    await waitFor(() => expect(window.location.pathname + window.location.search + window.location.hash).toBe('/ativar'));
    expect(window.history.length).toBe(tamanhoAntes);
    expect(JSON.stringify(window.history.state)).not.toContain('segredo-real');
    await preencherSenhas('senha-segura-123', 'senha-segura-123');
    await u.click(screen.getByRole('button', { name: 'Ativar acesso' }));
    await waitFor(() => expect(ativarFuncionario).toHaveBeenCalledWith({ token: 'segredo-real', senha: 'senha-segura-123' }));
    expect(await screen.findByText('LOGIN ATIVADO')).toBeInTheDocument();
  } finally {
    view.unmount();
    window.history.replaceState(null, '', '/');
  }
});
