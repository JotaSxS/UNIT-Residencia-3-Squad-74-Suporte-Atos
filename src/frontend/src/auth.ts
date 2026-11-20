// Conta de acesso (login simulado; com o SSO o papel virá do token).
import type { Role } from './types';

export type PapelLogin = Extract<Role, 'admin' | 'agente'>;

/**
 * Existem dois tipos de conta: administrador e membro da equipe.
 * Enquanto o login é simulado: e-mail que começa com "admin" entra como administrador; os demais, como equipe.
 */
export function papelPorEmail(email: string): PapelLogin {
  return email.trim().toLowerCase().startsWith('admin') ? 'admin' : 'agente';
}

/** "ana.souza@empresa.com" -> "Ana Souza". */
export function nomeDoEmail(email: string): string {
  return email.trim().split('@')[0].split('.').filter(Boolean)
    .map((p) => p.charAt(0).toUpperCase() + p.slice(1)).join(' ');
}
