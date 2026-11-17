import { expect, test } from '@playwright/test';
import { AGENT, openTicketFromQueue, TICKET_ID, TICKET_TITLE, loginAsAgent } from './helpers/app';

test.describe('Ciclo de vida do chamado (agente)', () => {
  test('abrir → atribuir → responder → fechar', async ({ page }) => {
    await loginAsAgent(page);

    // ---- Abrir: a fila mostra o chamado aberto e sem responsável ----
    await openTicketFromQueue(page, TICKET_ID);

    await expect(page.getByText(TICKET_ID, { exact: true })).toBeVisible();
    await expect(page.getByRole('heading', { level: 1, name: TICKET_TITLE })).toBeVisible();

    const statusChip = page.locator('#main-content .MuiChip-root');
    await expect(statusChip).toHaveText('Aberto');
    await expect(page.getByText('Sem agente', { exact: true }).first()).toBeVisible();

    // ---- Atribuir: o agente assume o chamado ----
    await page.getByRole('button', { name: 'Assumir chamado' }).click();

    await expect(page.getByText('Você assumiu o chamado.')).toBeVisible();
    await expect(page.getByText(`${AGENT.nome} (você)`)).toBeVisible();
    await expect(page.getByRole('button', { name: 'Assumir chamado' })).toHaveCount(0);
    await expect(statusChip).toHaveText('Em andamento');

    // ---- Responder: mensagem enviada ao cliente ----
    const reply = 'Estamos analisando o erro ao fechar a visita no Joe SFA B1. Retornamos assim que a correção for aplicada.';
    await page.getByLabel(/^Mensagem/).fill(reply);
    await page.getByRole('button', { name: 'Enviar' }).click();

    await expect(page.getByText('Resposta enviada ao cliente.')).toBeVisible();
    await expect(page.getByText(reply)).toBeVisible();
    await expect(statusChip).toHaveText('Aguardando cliente');

    // ---- Fechar: status final do chamado ----
    await page.getByLabel(/^Status/).last().click();
    await page.getByRole('option', { name: 'Fechado', exact: true }).click();

    await expect(page.getByText('Status atualizado para "Fechado".')).toBeVisible();
    await expect(statusChip).toHaveText('Fechado');
  });
});
