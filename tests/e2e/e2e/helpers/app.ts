import { expect, type Page } from '@playwright/test';

/**
 * Credenciais usadas no E2E. O build atual aceita qualquer e-mail válido e
 * senha com 4+ caracteres (não há validação real contra o SSO), mas os valores
 * podem ser sobrescritos por variáveis de ambiente para rodar em outros ambientes.
 */
export const AGENT = {
  nome: 'Gabriel Guimaraes',
  email: process.env.E2E_AGENT_EMAIL || 'gabriel.guimaraes@atoscapital.com',
  password: process.env.E2E_AGENT_PASSWORD || 'e2e-playwright',
};

/** Chamado do seed: aberto, sem agente — ponto de partida do ciclo de vida. */
export const TICKET_ID = process.env.E2E_TICKET_ID || 'AC-2026-00213';
export const TICKET_TITLE = 'Erro de sistema · Joe SFA B1';

/**
 * O badge do Netlify fica fixo no canto inferior direito e pode cobrir menus
 * (por exemplo, o select de status). Escondemos o iframe para não interceptar cliques.
 */
async function hideNetlifyBadge(page: Page): Promise<void> {
  await page.addInitScript(() => {
    const css = '#nl-badge-frame, iframe[title="Powered by Netlify"] { display: none !important; visibility: hidden !important; }';
    const apply = () => {
      if (document.getElementById('e2e-hide-netlify-badge')) return;
      const style = document.createElement('style');
      style.id = 'e2e-hide-netlify-badge';
      style.textContent = css;
      document.head?.appendChild(style);
    };
    if (document.readyState === 'loading') {
      document.addEventListener('DOMContentLoaded', apply, { once: true });
    } else {
      apply();
    }
  });
}

/** Abre a aplicação, autentica como agente e confirma que a fila carregou. */
export async function loginAsAgent(page: Page): Promise<void> {
  await hideNetlifyBadge(page);
  await page.goto('/');

  await expect(page).toHaveURL(/\/login$/);
  // O login atual é único: o papel de agente é inferido do e-mail (qualquer e-mail
  // que não comece com "admin" é membro da equipe); não há mais abas de perfil.
  await page.getByLabel(/E-mail corporativo/).fill(AGENT.email);
  await page.getByLabel(/^Senha/).fill(AGENT.password);
  await page.getByRole('button', { name: 'Entrar' }).click();

  await expect(page).toHaveURL(/\/chamados$/);
  await expect(page.getByRole('heading', { level: 1, name: 'Fila de atendimento' })).toBeVisible();
}

/** Abre um chamado a partir da fila usando o botão acessível da tabela. */
export async function openTicketFromQueue(page: Page, ticketId: string): Promise<void> {
  await page.getByRole('button', { name: `Abrir chamado ${ticketId}` }).click();
  await expect(page).toHaveURL(new RegExp(`/chamados/${ticketId}$`));
}
