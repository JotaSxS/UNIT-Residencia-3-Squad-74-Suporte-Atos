import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { ThemeProvider } from '@mui/material/styles';
import { LocalizationProvider } from '@mui/x-date-pickers/LocalizationProvider';
import { AdapterDayjs } from '@mui/x-date-pickers/AdapterDayjs';
import { beforeEach, expect, it, vi } from 'vitest';
import i18n from '../i18n';
import { seed } from '../data';
import { buildTheme } from '../theme';
import TicketList from './TicketList';
import TicketDetail from './TicketDetail';
import type { Session, ThemeMode } from '../types';

beforeEach(async () => { await i18n.changeLanguage('pt'); });

it.each<ThemeMode>(['light', 'dark'])('lista mantém busca, escopo e abertura no tema %s', (mode) => {
  const ticket = seed[0];
  const user: Session = { nome: ticket.contato, empresa: ticket.empresa, role: 'cliente' };
  const onOpen = vi.fn();
  render(
    <ThemeProvider theme={buildTheme(mode)}>
      <LocalizationProvider dateAdapter={AdapterDayjs}>
        <TicketList tickets={seed} user={user} role="cliente" onOpen={onOpen} onNew={vi.fn()} />
      </LocalizationProvider>
    </ThemeProvider>,
  );
  const search = screen.getByRole('searchbox', { name: 'Buscar chamados' });
  fireEvent.change(search, { target: { value: 'inexistente-xyz' } });
  expect(screen.queryAllByRole('button', { name: /^Abrir chamado AC-/ })).toHaveLength(0);
  fireEvent.change(search, { target: { value: ticket.id } });
  fireEvent.click(screen.getByRole('button', { name: i18n.t('tickets.openTicket', { id: ticket.id }) }));
  expect(onOpen).toHaveBeenCalledWith(ticket.id);
  const otherCompany = seed.find(item => item.empresa !== user.empresa)!;
  fireEvent.change(search, { target: { value: otherCompany.id } });
  expect(screen.queryByRole('button', { name: i18n.t('tickets.openTicket', { id: otherCompany.id }) })).not.toBeInTheDocument();
});

it.each<ThemeMode>(['light', 'dark'])('detalhe mantém resposta e transição do cliente no tema %s', async (mode) => {
  const ticket = seed[0];
  const user: Session = { nome: ticket.contato, empresa: ticket.empresa, role: 'cliente' };
  const onUpdate = vi.fn();
  render(
    <ThemeProvider theme={buildTheme(mode)}>
      <TicketDetail ticket={ticket} user={user} role="cliente" onBack={vi.fn()} onUpdate={onUpdate} />
    </ThemeProvider>,
  );
  fireEvent.change(screen.getByRole('textbox', { name: /Mensagem/ }), { target: { value: 'Atualização do cliente' } });
  fireEvent.click(screen.getByRole('button', { name: 'Enviar' }));
  await waitFor(() => expect(onUpdate).toHaveBeenCalledOnce());
  const updated = onUpdate.mock.calls[0][0];
  expect(updated.status).toBe('em_andamento');
  expect(updated.mensagens).toHaveLength(ticket.mensagens.length + 1);
  expect(updated.mensagens.at(-1)).toMatchObject({ autor: user.nome, tipo: 'cliente', texto: 'Atualização do cliente' });
  expect(screen.getByRole('textbox', { name: /Mensagem/ })).toHaveValue('');
});
