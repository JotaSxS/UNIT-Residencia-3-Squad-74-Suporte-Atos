
import { expect, type Page } from '@playwright/test';

/**
 * Credenciais sintéticas utilizadas exclusivamente nos testes E2E.
 * A autenticação é simulada pelo Playwright, sem alterar o backend
 * ou o mecanismo real de login da aplicação.
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
 * Esconde o badge do Netlify para não interceptar cliques
 * durante os testes de interface.
 */
async function hideNetlifyBadge(page: Page): Promise<void> {
  await page.addInitScript(() => {
    const css =
      '#nl-badge-frame, iframe[title="Powered by Netlify"] { display: none !important; visibility: hidden !important; }';

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

/**
 * Abre a aplicação, simula a resposta de autenticação
 * e confirma que a fila de atendimento foi carregada.
 */
export async function loginAsAgent(page: Page): Promise<void> {
  await hideNetlifyBadge(page);

  // Intercepta somente a autenticação durante o teste E2E.
  // Nenhuma alteração é feita na autenticação de produção.
  await page.route('**/api/auth/login', async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        token: 'e2e-synthetic-token',
        expiraEm: new Date(Date.now() + 60 * 60 * 1000).toISOString(),
        usuarioId: 1,
        nome: AGENT.nome,
        email: AGENT.email,
        papel: 'Agente',
        grupoEmpresaId: null,
      }),
    });
  });

  await page.goto('/');

  await expect(page).toHaveURL(/\/login$/);

  await page.getByLabel(/E-mail corporativo/).fill(AGENT.email);
  await page.getByLabel(/^Senha/).fill(AGENT.password);
  await page.getByRole('button', { name: 'Entrar' }).click();

  await expect(page).toHaveURL(/\/chamados$/);

  await expect(
    page.getByRole('heading', {
      level: 1,
      name: 'Fila de atendimento',
    }),
  ).toBeVisible();
}

/** Abre um chamado a partir da fila usando o botão acessível da tabela. */
export async function openTicketFromQueue(
  page: Page,
  ticketId: string,
): Promise<void> {
  await page.getByRole('button', {
    name: `Abrir chamado ${ticketId}`,
  }).click();

  await expect(page).toHaveURL(
    new RegExp(`/chamados/${ticketId}$`),
  );
}
