import React from 'react';
import { act, render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, expect, it, vi } from 'vitest';
import type { Session } from './types';
import i18n from './i18n';
import SupportPortal from './SupportPortal';

const auth = vi.hoisted(() => ({
  state: { status: 'restoring', session: null as Session | null },
  listeners: new Set<() => void>(),
  restore: vi.fn(),
}));
vi.mock('./auth', () => ({
  getAuthState: () => auth.state,
  subscribeAuth: (listener: () => void) => { auth.listeners.add(listener); return () => auth.listeners.delete(listener); },
  restoreSession: auth.restore,
  logout: vi.fn(),
  ativarFuncionario: vi.fn(),
}));
vi.mock('./components/Login', () => ({ default: () => <div>Login público</div> }));
vi.mock('./components/Layout', () => ({ Sidebar: () => <div>Menu protegido</div>, TopBar: () => null }));
vi.mock('./components/TicketList', () => ({ default: () => <div>Chamados protegidos</div>, NewTicketModal: () => null }));
vi.mock('./components/TicketDetail', () => ({ default: () => null }));
vi.mock('./components/Reports', () => ({ default: () => null }));
vi.mock('./components/UserAdmin', () => ({ default: () => <div>Admin protegido</div> }));
beforeEach(async () => {
  await i18n.changeLanguage('pt');
  auth.state = { status: 'restoring', session: null };
  auth.listeners.clear(); auth.restore.mockClear();
});
it('não expõe login nem telas protegidas enquanto restaura; conserva a rota após restaurar', async () => {
  render(<MemoryRouter initialEntries={['/usuarios']}><SupportPortal /></MemoryRouter>);
  expect(screen.queryByText('Menu protegido')).not.toBeInTheDocument();
  expect(screen.queryByText('Admin protegido')).not.toBeInTheDocument();
  expect(screen.queryByText('Login público')).not.toBeInTheDocument();
  expect(auth.restore).toHaveBeenCalled();
  act(() => {
    auth.state = { status: 'authenticated', session: { usuarioId: 1, role: 'admin', nome: 'Servidor', empresa: null } };
    auth.listeners.forEach(listener => listener());
  });
  expect(screen.getByText('Menu protegido')).toBeInTheDocument();
  expect(await screen.findByText('Admin protegido')).toBeInTheDocument();
});
it('abre /ativar sem sessão, sem passar pelo login', async () => {
  auth.state = { status: 'anonymous', session: null };
  render(<MemoryRouter initialEntries={['/ativar?token=convite']}><SupportPortal /></MemoryRouter>);

  expect(await screen.findByRole('heading', { name: 'Defina sua primeira senha' })).toBeInTheDocument();
  expect(screen.queryByText('Login público')).not.toBeInTheDocument();
});

it('falha na restauração mostra somente o login', () => {
  render(<MemoryRouter initialEntries={['/usuarios']}><SupportPortal /></MemoryRouter>);
  act(() => {
    auth.state = { status: 'anonymous', session: null };
    auth.listeners.forEach(listener => listener());
  });
  expect(screen.getByText('Login público')).toBeInTheDocument();
  expect(screen.queryByText('Menu protegido')).not.toBeInTheDocument();
});

it.each(['/usuarios', '/rota-inexistente', '/chamados/inexistente'])(
  'mantem navegacao interna e retorna a lista para cliente em %s', async (path) => {
    auth.state = { status: 'authenticated', session: { usuarioId: 2, role: 'cliente', nome: 'Cliente', empresa: 'Empresa' } };
    render(<MemoryRouter initialEntries={[path]}><SupportPortal /></MemoryRouter>);
    expect(await screen.findByText('Chamados protegidos')).toBeInTheDocument();
    expect(screen.queryByText('Admin protegido')).not.toBeInTheDocument();
    expect(screen.queryByText('Login público')).not.toBeInTheDocument();
  },
);

it('exibe ativacao mesmo com restauracao pendente', async () => {
  render(<MemoryRouter initialEntries={['/ativar#token=convite']}><SupportPortal /></MemoryRouter>);
  expect(await screen.findByRole('heading', { name: 'Defina sua primeira senha' })).toBeInTheDocument();
  expect(screen.queryByText('Login público')).not.toBeInTheDocument();
});
