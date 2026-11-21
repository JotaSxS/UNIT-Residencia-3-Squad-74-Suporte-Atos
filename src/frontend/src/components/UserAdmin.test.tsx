// Administração: cadastro de funcionários internos (Admin, Supervisor e Agente) sem grupo de empresa.
import React from 'react';
import { beforeAll, beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { toast } from 'sonner';
import userEvent from '@testing-library/user-event';
import { ThemeProvider } from '@mui/material/styles';
import i18n from '../i18n';
import { buildTheme } from '../theme';
import { ApiError, cadastrarUsuario } from '../api';
import UserAdmin from './UserAdmin';

// Só a rede é trocada por mocks; ApiError e o restante do cliente continuam os reais.
vi.mock('../api', async (importOriginal) => {
  const original = await importOriginal<typeof import('../api')>();
  return { ...original, cadastrarUsuario: vi.fn() };
});
vi.mock('sonner', () => ({ toast: { success: vi.fn(), error: vi.fn(), warning: vi.fn() } }));

const CONVITE = {
  id: 10,
  nome: 'Maria Silva',
  email: 'maria@atos.com',
  grupoEmpresaId: null,
  grupoEmpresaNome: null,
  papel: 'Supervisor' as const,
  tokenAtivacao: 'token-de-convite-com-tamanho-suficiente-para-o-contrato',
  expiraEm: '2026-11-21T12:00:00Z',
};
const inviteLink = `${window.location.origin}/ativar#token=${encodeURIComponent(CONVITE.tokenAtivacao)}`;

// Estes cenarios validam o valor final, nao eventos por tecla. Colar evita
// dezenas de renders MUI/RHF por campo e tarefas de digitacao apos um timeout.
async function preencherTexto(u: ReturnType<typeof userEvent.setup>, campo: HTMLElement, valor: string) {
  await u.click(campo);
  await u.paste(valor);
}

function renderAdmin() {
  render(
    <ThemeProvider theme={buildTheme('light')}>
      <UserAdmin />
    </ThemeProvider>,
  );
}

async function preencherFuncionario(u: ReturnType<typeof userEvent.setup>, papel = 'Agente') {
  await preencherTexto(u, screen.getByLabelText(/nome completo/i), 'Maria Silva');
  await preencherTexto(u, screen.getByLabelText(/e-mail/i), 'maria@atos.com');
  if (papel !== 'Agente') {
    await u.click(screen.getByRole('combobox', { name: /perfil/i }));
    await u.click(await screen.findByRole('option', { name: papel === 'Admin' ? 'Administrador' : papel }));
  }
}

describe('UserAdmin — cadastro de funcionários', () => {
  beforeAll(async () => { await i18n.changeLanguage('pt'); });
  beforeEach(() => { vi.resetAllMocks(); });

  it('oferece somente os papéis internos, sem Cliente', async () => {
    const u = userEvent.setup();
    renderAdmin();

    await u.click(screen.getByRole('combobox', { name: /perfil/i }));

    expect(await screen.findByRole('option', { name: 'Agente' })).toBeInTheDocument();
    expect(screen.getByRole('option', { name: 'Supervisor' })).toBeInTheDocument();
    expect(screen.getByRole('option', { name: 'Administrador' })).toBeInTheDocument();
    expect(screen.queryByRole('option', { name: 'Cliente' })).not.toBeInTheDocument();
  });

  it('não envia sem nome e e-mail, mostrando os erros sob os campos', async () => {
    const u = userEvent.setup();
    renderAdmin();

    await u.click(screen.getByRole('button', { name: 'Cadastrar funcionário' }));

    const nome = screen.getByLabelText(/nome completo/i);
    const email = screen.getByLabelText(/e-mail/i);
    await waitFor(() => expect(nome).toHaveAccessibleDescription('Campo obrigatório.'));
    expect(email).toHaveAccessibleDescription('Campo obrigatório.');
    expect(cadastrarUsuario).not.toHaveBeenCalled();
  });

  it('recusa e-mail inválido antes de chamar a API', async () => {
    const u = userEvent.setup();
    renderAdmin();

    await preencherTexto(u, screen.getByLabelText(/nome completo/i), 'Maria Silva');
    await preencherTexto(u, screen.getByLabelText(/e-mail/i), 'maria');
    await u.click(screen.getByRole('button', { name: 'Cadastrar funcionário' }));

    const email = screen.getByLabelText(/e-mail/i);
    await waitFor(() => expect(email).toHaveAccessibleDescription('Informe um e-mail válido.'));
    expect(cadastrarUsuario).not.toHaveBeenCalled();
  });

  it.each(['Admin', 'Supervisor', 'Agente'] as const)('envia %s com grupoEmpresaId null e sem campo de grupo', async (papel) => {
    vi.mocked(cadastrarUsuario).mockResolvedValue({ ...CONVITE, papel });
    const u = userEvent.setup();
    renderAdmin();

    await preencherFuncionario(u, papel);
    await u.click(screen.getByRole('button', { name: 'Cadastrar funcionário' }));

    await waitFor(() => expect(cadastrarUsuario).toHaveBeenCalledWith({
      nome: 'Maria Silva',
      email: 'maria@atos.com',
      grupoEmpresaId: null,
      papel,
    }));
    expect(screen.queryByLabelText(/grupo/i)).not.toBeInTheDocument();
    await waitFor(() => expect(toast.success).toHaveBeenCalledWith('Funcionário Maria Silva cadastrado.'));
    expect(screen.getByLabelText(/nome completo/i)).toHaveValue('');
    expect(screen.getByLabelText(/e-mail/i)).toHaveValue('');
    expect(screen.getByDisplayValue(inviteLink)).toBeInTheDocument();
  });

  it('exibe o convite somente após sucesso, copia o link e não persiste nada', async () => {
    vi.mocked(cadastrarUsuario).mockResolvedValue(CONVITE);
    const u = userEvent.setup();
    const clipboardWrite = vi.spyOn(navigator.clipboard, 'writeText').mockResolvedValue(undefined);
    renderAdmin();

    expect(screen.queryByLabelText('Link de ativação')).not.toBeInTheDocument();
    const storageAntes = [JSON.stringify(localStorage), JSON.stringify(sessionStorage)];

    await preencherFuncionario(u);
    await u.click(screen.getByRole('button', { name: 'Cadastrar funcionário' }));

    const linkField = await screen.findByLabelText('Link de ativação');
    expect(linkField).toHaveValue(inviteLink);
    expect(screen.getByText(/Válido até/)).toBeInTheDocument();
    expect(screen.getByText(/uso único/i)).toBeInTheDocument();

    await u.click(screen.getByRole('button', { name: 'Copiar link' }));
    await waitFor(() => expect(clipboardWrite).toHaveBeenCalledWith(inviteLink));
    expect(await screen.findByText('Link copiado.')).toBeInTheDocument();
    expect([JSON.stringify(localStorage), JSON.stringify(sessionStorage)]).toEqual(storageAntes);
  });

  it('mantém o link para cópia manual quando a Clipboard API falha', async () => {
    vi.mocked(cadastrarUsuario).mockResolvedValue(CONVITE);
    const u = userEvent.setup();
    vi.spyOn(navigator.clipboard, 'writeText').mockRejectedValue(new Error('negado'));
    renderAdmin();

    await preencherFuncionario(u);
    await u.click(screen.getByRole('button', { name: 'Cadastrar funcionário' }));
    const linkField = await screen.findByLabelText('Link de ativação');
    await u.click(screen.getByRole('button', { name: 'Copiar link' }));

    expect(await screen.findByText(/Selecione o link e copie manualmente/)).toBeInTheDocument();
    expect(linkField).toHaveValue(inviteLink);
  });

  it('preserva o convite e bloqueia outro cadastro ate confirmacao explicita', async () => {
    vi.mocked(cadastrarUsuario).mockResolvedValueOnce(CONVITE);
    const u = userEvent.setup();
    renderAdmin();
    await preencherFuncionario(u);
    await u.click(screen.getByRole('button', { name: 'Cadastrar funcionário' }));
    expect(await screen.findByDisplayValue(inviteLink)).toBeInTheDocument();
    const submit = screen.getByRole('button', { name: 'Cadastrar funcionário' });
    expect(submit).toBeDisabled();
    fireEvent.click(submit);
    expect(cadastrarUsuario).toHaveBeenCalledTimes(1);
    await u.click(screen.getByRole('button', { name: /Confirmei a cópia ou descarte/ }));
    expect(screen.queryByDisplayValue(inviteLink)).not.toBeInTheDocument();
    vi.mocked(cadastrarUsuario).mockRejectedValueOnce(new ApiError('Falha', 500));
    await preencherFuncionario(u);
    await u.click(submit);
    await waitFor(() => expect(cadastrarUsuario).toHaveBeenCalledTimes(2));
    expect(toast.success).toHaveBeenCalledTimes(1);
  });

  it('resposta sem convite nao anuncia sucesso nem limpa o formulario', async () => {
    vi.mocked(cadastrarUsuario).mockResolvedValue({ ...CONVITE, tokenAtivacao: '' });
    const u = userEvent.setup();
    renderAdmin();
    await preencherFuncionario(u);
    await u.click(screen.getByRole('button', { name: 'Cadastrar funcionário' }));
    await waitFor(() => expect(toast.error).toHaveBeenCalled());
    expect(toast.success).not.toHaveBeenCalled();
    expect(screen.queryByLabelText('Link de ativação')).not.toBeInTheDocument();
    expect(screen.getByLabelText(/nome completo/i)).toHaveValue('Maria Silva');
  });

  it('mostra e-mail já cadastrado no campo quando a API responde 409', async () => {
    vi.mocked(cadastrarUsuario).mockRejectedValue(new ApiError('E-mail já cadastrado.', 409));
    const u = userEvent.setup();
    renderAdmin();

    await preencherFuncionario(u);
    await u.click(screen.getByRole('button', { name: 'Cadastrar funcionário' }));

    const email = screen.getByLabelText(/e-mail/i);
    await waitFor(() => expect(email).toHaveAccessibleDescription('E-mail já cadastrado.'));
  });

  it.each([401, 403])('nao anuncia sucesso quando a API recusa com %s', async (status) => {
    vi.mocked(cadastrarUsuario).mockRejectedValue(new ApiError('Acesso negado.', status));
    const u = userEvent.setup();
    renderAdmin();
    await preencherFuncionario(u);
    await u.click(screen.getByRole('button', { name: 'Cadastrar funcionário' }));
    await waitFor(() => expect(toast.warning).toHaveBeenCalledWith('Acesso negado.'));
    expect(toast.success).not.toHaveBeenCalled();
    expect(screen.getByLabelText(/nome completo/i)).toHaveValue('Maria Silva');
  });

  it('recusa nome maior que a coluna de 150 caracteres', async () => {
    const u = userEvent.setup();
    renderAdmin();
    await u.click(screen.getByLabelText(/nome completo/i));
    await u.paste('A'.repeat(151));
    await u.click(screen.getByRole('button', { name: 'Cadastrar funcionário' }));
    await waitFor(() => expect(screen.getByLabelText(/nome completo/i)).toHaveAccessibleDescription('Use no máximo 150 caracteres.'));
    expect(cadastrarUsuario).not.toHaveBeenCalled();
  });
});
