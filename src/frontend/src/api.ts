// Cliente HTTP autenticado. JWT em memória; cookie HttpOnly apenas nos endpoints de autenticação.
import i18n from './i18n';
import { ApiError, responseError, send } from './http';
import { ensureAccessToken, expireSession, getAuthEpoch, getTokenVersion, refreshSession } from './auth';
import { parseApiDate } from './dates';
import type { PriorityKey, Session, StatusKey, Ticket } from './types';

export const TITULO_MAX = 200;

/* ---------------------------------------------------------------------- */
/* Contratos da API (espelham os DTOs do backend)                          */
/* ---------------------------------------------------------------------- */

export type PapelApi = 'Cliente' | 'Agente' | 'Supervisor' | 'Admin';
/** Papéis de funcionários internos: podem ser cadastrados sem grupo de empresa e recebem convite de ativação. */
export type PapelInterno = Exclude<PapelApi, 'Cliente'>;

export interface SlaCategoriaDto {
  produto: string;
  categoria: string;
  prioridade: string;
}

export interface ChamadoDto {
  id: number;
  codigoPublico: string;
  produto: string;
  categoria: string;
  titulo: string;
  descricao: string;
  status: string;
  prioridade: string;
  slaPrazo: string;
  criadoEm: string;
}

export interface NovoChamadoInput {
  titulo: string;
  produto: string;
  categoria: string;
  descricao: string;
}

export interface UsuarioDto {
  id: number;
  nome: string;
  email: string;
  grupoEmpresaId: number | null;
  grupoEmpresaNome: string | null;
  papel: PapelApi;
}

/** Resposta de POST /api/usuarios para Admin, Supervisor e Agente (convite de ativação). */
export interface ConviteFuncionarioDto extends UsuarioDto {
  papel: PapelInterno;
  tokenAtivacao: string;
  expiraEm: string;
}

/** Distingue a resposta de convite (funcionário interno) da resposta de cliente. */
export function isConviteFuncionario(
  usuario: UsuarioDto | ConviteFuncionarioDto,
): usuario is ConviteFuncionarioDto {
  return usuario !== null && typeof usuario === 'object' && 'tokenAtivacao' in usuario
    && typeof usuario.tokenAtivacao === 'string' && usuario.tokenAtivacao.trim().length > 0
    && Number.isInteger(usuario.id) && usuario.id > 0
    && typeof usuario.nome === 'string' && usuario.nome.trim().length > 0
    && 'expiraEm' in usuario && typeof usuario.expiraEm === 'string'
    && Number.isFinite(Date.parse(usuario.expiraEm))
    && ['Admin', 'Supervisor', 'Agente'].includes(usuario.papel);
}

export interface NovoUsuarioInput {
  nome: string;
  email: string;
  /** Obrigatório apenas para Cliente; funcionários internos enviam null. */
  grupoEmpresaId: number | null;
  papel: PapelApi;
}

/* ---------------------------------------------------------------------- */
/* Transporte                                                              */
/* ---------------------------------------------------------------------- */

export { ApiError } from './http';

async function request<T>(path: string, { method = 'GET', body }: { method?: string; body?: unknown } = {}): Promise<T> {
  let token = await ensureAccessToken();
  const epoch = getAuthEpoch();
  const observedVersion = getTokenVersion();
  for (let attempt = 0; attempt < 2; attempt++) {
    if (epoch !== getAuthEpoch()) throw new ApiError(i18n.t('api.unauthorized'), 401);
    const response = await send(path, {
      method,
      headers: { Accept: 'application/json', Authorization: `Bearer ${token}`,
        ...(body === undefined ? {} : { 'Content-Type': 'application/json' }) },
      ...(body === undefined ? {} : { body: JSON.stringify(body) }),
    });
    if (epoch !== getAuthEpoch()) throw new ApiError(i18n.t('api.unauthorized'), 401);
    if (response.status === 401 && attempt === 0) {
      await refreshSession(observedVersion);
      if (epoch !== getAuthEpoch()) throw new ApiError(i18n.t('api.unauthorized'), 401);
      token = await ensureAccessToken();
      continue;
    }
    if (response.status === 401) expireSession(epoch);
    if (!response.ok) throw await responseError(response);
    const payload = response.status === 204 ? null : await response.json();
    if (epoch !== getAuthEpoch()) throw new ApiError(i18n.t('api.unauthorized'), 401);
    return payload as T;
  }
  throw new ApiError(i18n.t('api.unauthorized'), 401);
}
/** Opções reais de produto/categoria (tabela sla_categoria). */
export const listarSlaCategorias = () => request<SlaCategoriaDto[]>('/api/sla-categorias');

/** Abre um chamado. `titulo` é obrigatório (validado também no servidor). */
export const criarChamado = ({ titulo, produto, categoria, descricao }: NovoChamadoInput) =>
  request<ChamadoDto>('/api/chamados', {
    method: 'POST',
    body: { titulo: titulo.trim(), produto, categoria, mensagemInicial: descricao.trim() },
  });

/* ---------------------------------------------------------------------- */
/* Administração de usuários (perfil Admin)                                */
/* ---------------------------------------------------------------------- */

export const listarUsuariosDoGrupo = (grupoEmpresaId: number) =>
  request<UsuarioDto[]>(`/api/usuarios?grupoEmpresaId=${encodeURIComponent(grupoEmpresaId)}`);

/** Cliente retorna UsuarioDto; funcionário interno retorna ConviteFuncionarioDto. */
export const cadastrarUsuario = ({ nome, email, grupoEmpresaId, papel }: NovoUsuarioInput) =>
  request<UsuarioDto | ConviteFuncionarioDto>('/api/usuarios', {
    method: 'POST',
    body: { nome: nome.trim(), email: email.trim().toLowerCase(), grupoEmpresaId, papel },
  });

/* ---------------------------------------------------------------------- */
/* Mapeamento para o modelo das telas                                      */
/* ---------------------------------------------------------------------- */

const HORA = 3600 * 1000;
const PRIORIDADE: Record<string, PriorityKey> = { alta: 'alta', media: 'media', baixa: 'baixa' };
const STATUS: Record<string, StatusKey> = {
  aberto: 'aberto', emandamento: 'em_andamento', aguardandocliente: 'aguardando_cliente', resolvido: 'resolvido', fechado: 'fechado',
};
const normalizar = (v: string) => v.normalize('NFD').replace(/[\u0300-\u036f_\s]/g, '').toLowerCase();

/** ChamadoDto da API -> formato de chamado usado pelas telas. */
export function chamadoDtoParaTicket(dto: ChamadoDto, user: Session): Ticket {
  const prazo = parseApiDate(dto.slaPrazo);
  const criado = parseApiDate(dto.criadoEm);
  const total = Math.max(1, Math.round((prazo.valueOf() - criado.valueOf()) / HORA));
  const restante = Math.max(0, Math.round((prazo.valueOf() - Date.now()) / HORA));
  return {
    id: dto.codigoPublico,
    empresa: user.empresa ?? '—',
    contato: user.nome,
    produto: dto.produto,
    categoria: dto.categoria,
    assunto: dto.titulo,
    prioridade: PRIORIDADE[normalizar(dto.prioridade)] ?? 'media',
    status: STATUS[normalizar(dto.status)] ?? 'aberto',
    agente: null,
    slaTotal: total,
    slaRestante: Math.min(total, restante),
    abertura: criado.toISOString(),
    mensagens: [{ autor: user.nome, tipo: 'cliente', data: criado.toISOString(), texto: dto.descricao }],
    notas: [],
  };
}
