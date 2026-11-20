import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

const result = (token = 'access-a', papel = 'Agente', seconds = 600) => ({
  token, expiraEm: new Date(Date.now() + seconds * 1000).toISOString(),
  usuarioId: 42, nome: 'Nome do backend', email: 'admin@qa.invalid', papel, grupoEmpresaId: 7,
});
const json = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } });
const deferred = <T,>() => {
  let resolve!: (value: T) => void;
  const promise = new Promise<T>(r => { resolve = r; });
  return { promise, resolve };
};
let auth: typeof import('./auth');
let api: typeof import('./api');
let fetchMock = vi.fn<typeof fetch>();
beforeEach(async () => {
  vi.resetModules();
  localStorage.clear(); sessionStorage.clear();
  fetchMock = vi.fn<typeof fetch>();
  vi.stubGlobal('fetch', fetchMock);
  auth = await import('./auth');
  api = await import('./api');
});
afterEach(() => { vi.unstubAllGlobals(); vi.restoreAllMocks(); });
async function loggedIn(seconds = 600) {
  fetchMock.mockResolvedValueOnce(json(result('access-a', 'Agente', seconds)));
  await auth.login('admin@qa.invalid', 'senha de teste');
  fetchMock.mockClear();
}

describe('autenticação real em memória', () => {
  it('envia e-mail/senha e usa identidade/papel do backend, sem inferir pelo e-mail', async () => {
    fetchMock.mockResolvedValueOnce(json(result('access-a', 'Supervisor')));
    const session = await auth.login(' admin@qa.invalid ', ' senha com espaços ');
    expect(fetchMock).toHaveBeenCalledWith('https://localhost:7099/api/auth/login', expect.objectContaining({
      method: 'POST', credentials: 'include', body: JSON.stringify({ email: 'admin@qa.invalid', senha: ' senha com espaços ' }),
    }));
    expect(session).toMatchObject({ role: 'supervisor', usuarioId: 42, nome: 'Nome do backend', grupoEmpresaId: 7 });
    expect(auth.getToken()).toBe('access-a');
    expect(sessionStorage.getItem('atos_token')).toBeNull();
    expect(localStorage.getItem('atos_token')).toBeNull();
  });
  it.each([400, 401, 403])('login HTTP %s não estabelece sessão', async status => {
    fetchMock.mockResolvedValueOnce(json({ detail: 'Acesso recusado' }, status));
    await expect(auth.login('admin@qa.invalid', 'incorreta')).rejects.toMatchObject({ status });
    expect(auth.getToken()).toBe('');
    expect(auth.getAuthState()).toEqual({ status: 'anonymous', session: null });
    expect(fetchMock).toHaveBeenCalledTimes(1);
  });
  it('restaura pelo cookie uma vez mesmo com inicialização simultânea/StrictMode', async () => {
    const pending = deferred<Response>();
    fetchMock.mockReturnValueOnce(pending.promise);
    const first = auth.restoreSession();
    const second = auth.restoreSession();
    expect(auth.getAuthState().status).toBe('restoring');
    await vi.waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(1));
    pending.resolve(json(result()));
    expect(await first).toEqual(await second);
    expect(fetchMock.mock.calls[0][1]).toMatchObject({ method: 'POST', credentials: 'include' });
    expect(fetchMock.mock.calls[0][0]).toContain('/api/auth/refresh');
    expect(auth.getAuthState().status).toBe('authenticated');
  });
  it('após recarregar não depende de token em Storage e restaura a identidade', async () => {
    await loggedIn();
    vi.resetModules();
    auth = await import('./auth');
    expect(auth.getToken()).toBe('');
    fetchMock.mockResolvedValueOnce(json(result('access-restored')));
    await auth.restoreSession();
    expect(auth.getToken()).toBe('access-restored');
    expect(auth.getAuthState().session?.usuarioId).toBe(42);
  });
  it('cookie inválido encerra restauração e não gera tentativas em loop', async () => {
    fetchMock.mockResolvedValueOnce(json({ detail: 'Cookie expirado' }, 400));
    expect(await auth.restoreSession()).toBeNull();
    await auth.restoreSession();
    await expect(api.listarUsuariosDoGrupo(7)).rejects.toMatchObject({ status: 401 });
    expect(fetchMock).toHaveBeenCalledTimes(1);
  });
  it('renova preventivamente quando o JWT está próximo de expirar', async () => {
    await loggedIn(1);
    fetchMock.mockResolvedValueOnce(json(result('access-b'))).mockResolvedValueOnce(json([]));
    await api.listarUsuariosDoGrupo(7);
    expect(fetchMock.mock.calls[0][0]).toContain('/api/auth/refresh');
    expect(fetchMock.mock.calls[1][1]?.headers).toMatchObject({ Authorization: 'Bearer access-b' });
  });
  it('renova um JWT já expirado antes de enviar uma requisição protegida', async () => {
    await loggedIn();
    vi.spyOn(Date, 'now').mockReturnValue(Date.now() + 601000);
    fetchMock.mockResolvedValueOnce(json(result('access-b'))).mockResolvedValueOnce(json([]));
    await api.listarUsuariosDoGrupo(7);
    expect(fetchMock.mock.calls[0][0]).toContain('/api/auth/refresh');
    expect(fetchMock.mock.calls[1][1]?.headers).toMatchObject({ Authorization: 'Bearer access-b' });
  });
  it('falha de rede no login não concede acesso', async () => {
    fetchMock.mockRejectedValueOnce(new TypeError('offline'));
    await expect(auth.login('admin@qa.invalid', 'teste')).rejects.toMatchObject({ status: 0 });
    expect(auth.getAuthState().status).toBe('anonymous');
  });
  it('não aceita papel desconhecido na resposta do backend', async () => {
    fetchMock.mockResolvedValueOnce(json(result('access-a', 'toString')));
    await expect(auth.login('admin@qa.invalid', 'teste')).rejects.toMatchObject({ status: 401 });
    expect(auth.getToken()).toBe('');
  });
  it('coordena 401 simultâneos e tardios sem repetir refresh nem perder o Bearer novo', async () => {
    await loggedIn();
    const late = deferred<Response>();
    let oldRequests = 0;
    fetchMock.mockImplementation(async (url, options) => {
      if (String(url).endsWith('/auth/refresh')) return json(result('access-b'));
      if ((options?.headers as Record<string, string>).Authorization === 'Bearer access-a') {
        oldRequests++;
        return oldRequests === 1 ? json({}, 401) : late.promise;
      }
      return json([]);
    });
    const first = api.listarUsuariosDoGrupo(7);
    const second = api.listarUsuariosDoGrupo(7);
    await first;
    late.resolve(json({}, 401));
    await second;
    expect(fetchMock.mock.calls.filter(([url]) => String(url).endsWith('/auth/refresh'))).toHaveLength(1);
    expect(fetchMock).toHaveBeenCalledTimes(5);
  });
  it('compartilha um refresh ainda pendente entre várias requisições', async () => {
    await loggedIn(1);
    const pending = deferred<Response>();
    fetchMock.mockReturnValueOnce(pending.promise).mockImplementation(async () => json([]));
    const requests = [api.listarUsuariosDoGrupo(7), api.listarUsuariosDoGrupo(7), api.listarUsuariosDoGrupo(7)];
    await vi.waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(1));
    pending.resolve(json(result('access-b')));
    await Promise.all(requests);
    expect(fetchMock).toHaveBeenCalledTimes(4);
  });
  it('falha no refresh elimina sessão e impede novos retries', async () => {
    await loggedIn();
    fetchMock.mockResolvedValueOnce(json({}, 401)).mockResolvedValueOnce(json({}, 400));
    await expect(api.listarUsuariosDoGrupo(7)).rejects.toMatchObject({ status: 400 });
    await expect(api.listarUsuariosDoGrupo(7)).rejects.toMatchObject({ status: 401 });
    expect(fetchMock).toHaveBeenCalledTimes(2);
    expect(auth.getToken()).toBe('');
  });
  it('um segundo 401 após renovar encerra a sessão sem loop', async () => {
    await loggedIn();
    fetchMock.mockResolvedValueOnce(json({}, 401)).mockResolvedValueOnce(json(result('access-b'))).mockResolvedValueOnce(json({}, 401));
    await expect(api.listarUsuariosDoGrupo(7)).rejects.toMatchObject({ status: 401 });
    expect(fetchMock).toHaveBeenCalledTimes(3);
    expect(auth.getAuthState().status).toBe('anonymous');
  });
  it('403 mantém a sessão, propaga o erro e não tenta renovar', async () => {
    await loggedIn();
    fetchMock.mockResolvedValueOnce(json({ detail: 'Somente Admin' }, 403));
    await expect(api.listarUsuariosDoGrupo(7)).rejects.toMatchObject({ status: 403, message: 'Somente Admin' });
    expect(fetchMock).toHaveBeenCalledTimes(1);
    expect(auth.getAuthState().status).toBe('authenticated');
  });
  it.each([true, false])('logout limpa memória mesmo com falha de rede=%s', async failed => {
    await loggedIn();
    if (failed) fetchMock.mockRejectedValueOnce(new TypeError('offline'));
    else fetchMock.mockResolvedValueOnce(json({ mensagem: 'OK' }));
    const loggingOut = auth.logout();
    expect(auth.getToken()).toBe('');
    expect(auth.getAuthState().session).toBeNull();
    if (failed) await expect(loggingOut).rejects.toMatchObject({ status: 0 });
    else await loggingOut;
    expect(fetchMock.mock.calls[0][0]).toContain('/api/auth/logout');
    expect(fetchMock.mock.calls[0][1]?.credentials).toBe('include');
  });
  it('logout durante refresh invalida a resposta tardia e revoga o cookie depois dela', async () => {
    await loggedIn();
    const pending = deferred<Response>();
    fetchMock.mockReturnValueOnce(pending.promise).mockResolvedValueOnce(json({ mensagem: 'OK' }));
    const refreshing = auth.refreshSession().catch(error => error);
    await vi.waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(1));
    const loggingOut = auth.logout();
    pending.resolve(json(result('access-late')));
    await refreshing; await loggingOut;
    expect(auth.getToken()).toBe('');
    expect(auth.getAuthState().status).toBe('anonymous');
    expect(fetchMock.mock.calls.map(([url]) => String(url).split('/').pop())).toEqual(['refresh', 'logout']);
  });
  it('não reaplica uma resposta antiga sob a identidade de outro login', async () => {
    await loggedIn();
    const pending = deferred<Response>();
    fetchMock.mockReturnValueOnce(pending.promise);
    const oldRequest = api.listarUsuariosDoGrupo(7).catch(error => error);
    await vi.waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(1));
    fetchMock.mockResolvedValueOnce(json(result('access-other', 'Admin')));
    await auth.login('outro@qa.invalid', 'senha de teste');
    pending.resolve(json({}, 401));
    expect(await oldRequest).toMatchObject({ status: 401 });
    expect(auth.getToken()).toBe('access-other');
    expect(fetchMock).toHaveBeenCalledTimes(2);
  });
  it('descarta também um corpo HTTP 200 que termina de chegar depois do logout', async () => {
    await loggedIn();
    const body = deferred<unknown>();
    const response = json([]);
    vi.spyOn(response, 'json').mockReturnValue(body.promise);
    fetchMock.mockResolvedValueOnce(response).mockResolvedValueOnce(json({ mensagem: 'OK' }));
    const request = api.listarUsuariosDoGrupo(7).catch(error => error);
    await vi.waitFor(() => expect(response.json).toHaveBeenCalled());
    await auth.logout();
    body.resolve([]);
    expect(await request).toMatchObject({ status: 401 });
  });
});
