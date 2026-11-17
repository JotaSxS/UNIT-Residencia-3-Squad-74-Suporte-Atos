import { describe, expect, it } from 'vitest';
import { nomeDoEmail, papelPorEmail } from './auth';

describe('papelPorEmail', () => {
  it('e-mail que começa com "admin" entra como administrador', () => {
    expect(papelPorEmail('admin@atoscapital.com')).toBe('admin');
    expect(papelPorEmail('  ADMIN.chefe@atoscapital.com ')).toBe('admin');
  });

  it('os demais e-mails entram como membro da equipe', () => {
    expect(papelPorEmail('ana.souza@atoscapital.com')).toBe('agente');
  });
});

describe('nomeDoEmail', () => {
  it('monta o nome a partir da parte antes do @', () => {
    expect(nomeDoEmail('ana.souza@atoscapital.com')).toBe('Ana Souza');
  });
});
