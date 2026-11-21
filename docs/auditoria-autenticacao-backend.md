# Auditoria da autenticação própria do SupportAtos

Data: 19/11/2026. Escopo: backend .NET/EF Core/PostgreSQL e respectivos testes. As alterações preexistentes do patch foram preservadas. Frontend, API Key, provisionamento do portal, entidades e migrations não foram modificados nesta auditoria. Nenhum commit ou push foi realizado.

## Problemas comprovados e correções

| Natureza | Evidência no patch recebido | Correção |
|---|---|---|
| Vulnerabilidade crítica: exposição de credenciais | `RefreshAsync` imprimia token original, hash e identificadores da sessão em três `Console.WriteLine`. | Logs removidos. Busca estática nos pontos de logging do backend não identificou outra impressão direta de senha ou refresh token. Não há `EnableSensitiveDataLogging` no código inspecionado. Isso não audita configurações externas de proxy/APM ou logs históricos. |
| Vulnerabilidade alta: corrida na rotação | `ConcurrentDictionary<int, SemaphoreSlim>` sincronizava somente um processo; consulta EF rastreada podia ainda retornar estado anterior à espera. | Repositório existente ganhou operação transacional `ReadCommitted` com `pg_advisory_xact_lock(74101, usuarioId)`, parametrizado. Rotação, gravação do sucessor e commit ocorrem sob o mesmo bloqueio. Sessões já rastreadas são recarregadas após adquiri-lo. Falhas fazem rollback. |
| Vulnerabilidade alta: revogação global indevida | Qualquer repetição de token substituído revogava todas as sessões do usuário, inclusive logins independentes. Um teste existente também contradizia essa política. | Duplicatas próximas são rejeitadas sem revogação. Reuso tardio revoga apenas a cadeia de sucessores. Política detalhada abaixo; testes corrigidos para explicitar o intervalo. |
| Vulnerabilidade alta: CSRF | Ausência de `Origin` era aceita como evidência de mesma origem; `PATCH` não era protegido; login não estava na lista. | Login, refresh e logout exigem origem confiável nos métodos inseguros. Sem `Origin`, aceita-se apenas `Referer` HTTP(S) com origem permitida. Sem ambos, cabeçalho vazio, `null`, origem opaca, credenciais embutidas, múltiplas origens ou origem não permitida: 403. `Origin` inválido não pode ser contornado com `Referer` válido. |
| Falha de segurança: custo fictício do login inexistente | Hash PHC era rejeitado antes de executar Argon2, porque o verificador aceita `Base64(salt).Base64(hash)`. | Constante sintética no formato nativo, com salt de 16 bytes e resultado de 32 bytes. A verificação real executa Argon2id. Não corresponde a uma conta nem constitui segredo. |
| Falha funcional: autenticação anônima inacessível | Controller de autenticação não tinha `AllowAnonymous`, ficando sujeito à política global que exige autenticação. | Controller explicitamente anônimo; operações com cookie permanecem protegidas pelo CSRF e pela validação do refresh. |
| Falha funcional: credencial não carregada | Consultas por ID/e-mail não incluíam `Credencial`, sem lazy loading/AutoInclude configurado. Testes com fakes ocultavam o problema. | `Include(u => u.Credencial)` nessas duas consultas existentes. Fluxo real de login/refresh/JWT validado por HTTP. |
| Defesa adicional na autorização interna | Papel atual já era consultado, mas somente a claim configurada era removida; JWT próprio também usa `ClaimTypes.Role`. Claim de grupo podia continuar antiga. | Remove também `ClaimTypes.Role` e substitui grupo pela informação atual do banco. Mantido o contexto interno com papel/grupo atuais; nenhum ajuste no provisionamento externo. |
| Documentação incorreta | Factory de design-time prometia user-secrets, mas somente chamava `Environment.GetEnvironmentVariable`. | Documentação e erro agora informam que somente `ConnectionStrings__DefaultConnection` é lida. Não se adicionou carregamento implícito de segredos. |

O gerador CLI de API Key continua exibindo a chave uma vez quando explicitamente invocado; esse comportamento preexistente de provisionamento não foi alterado nem classificado como vazamento do fluxo de autenticação própria.

## Política de rotação, reuso e logout

- Apenas a primeira rotação de um refresh ativo tem sucesso. As demais recebem erro, sem recuperar ou devolver o token sucessor.
- Até 5 segundos após a revogação por rotação: repetição rejeitada, sem revogar o sucessor. A janela limita falsos positivos de chamadas concorrentes; não permite duas rotações bem-sucedidas.
- Após 5 segundos: repetição de token substituído revoga seus descendentes, percorrendo `SubstituidoPorRefreshTokenHash`. Outros logins não são revogados. A revogação é confirmada antes de lançar a exceção de validação.
- Logout usa o mesmo bloqueio e revoga o token e seus descendentes. Assim, um logout com cookie antigo concorrente com refresh não deixa o sucessor ativo. Logout repetido ou com token desconhecido continua idempotente.
- O protocolo de bloqueio é compartilhado pelo PostgreSQL entre conexões/processos. Todos os processos da API devem executar o código corrigido; versões antigas e escritores SQL que ignorem o bloqueio advisory não participam dessa garantia.

Não é possível distinguir perfeitamente uma duplicata legítima de um token roubado usando apenas o mesmo segredo bearer. Dentro da janela, um atacante que tenha vencido a primeira rotação não será identificado por uma única duplicata; depois da janela, uma tentativa legítima muito atrasada também será tratada como suspeita, afetando somente sua cadeia. Os relógios dos hosts devem estar sincronizados. A janela escolhida é uma política explícita, não uma prova de ausência de roubo.

**Logout não invalida access tokens já emitidos.** Eles continuam válidos até expiração mais o `ClockSkew` configurado, enquanto o usuário possuir credencial ativa, não estiver bloqueado e tiver papel interno permitido. Cada autenticação interna consulta o banco e aplica papel/grupo atuais. Desativar a credencial invalida tanto JWT quanto refresh. Não foi criada denylist nem revogação adicional de access tokens.

## CSRF, cookies e configuração

`Cors:AllowedOrigins` exige origens HTTP(S) explícitas, sem wildcard, caminho, query, fragmento, usuário/senha ou barra final. Esquema, host e porta são comparados por origem canônica; sufixos de domínio não são aceitos. A lista é compartilhada entre CORS e CSRF. A origem pública da própria API também precisa constar se clientes dela fizerem operações de autenticação. Não se deduz confiança de `Host`, `X-Forwarded-Host` ou da ausência de cabeçalhos.

CORS conserva credenciais apenas para origens explícitas; não substitui CSRF. Preflight OPTIONS passa sem validar cookie. Métodos seguros passam sem exigir origem. Clientes HTTP sem navegador precisam enviar `Origin` permitido ou `Referer` válido nessas operações.

Cookie mantém `HttpOnly`, caminho configurado e modo SameSite explícito. Configuração inválida falha ao iniciar. `Secure=false` somente é aceito em Development com Lax/Strict; `SameSite=None` sempre exige Secure. Lax/Strict comparam **sites**, não origens; deployments realmente cross-site precisam de None + HTTPS + CORS explícito. Não se mudou automaticamente o SameSite para acomodar uma topologia desconhecida.

Factory EF: definir `ConnectionStrings__DefaultConnection` no processo que invocar a ferramenta. User-secrets carregados pelo host da API em desenvolvimento não são carregados por essa factory. Apenas construir o contexto não aplica migrations.

## Validação executada

PostgreSQL 18 temporário iniciado em diretório exclusivo no TEMP, vinculado a `127.0.0.1:55479`, separado do servidor/banco da aplicação. A suíte recebeu `SUPPORTATOS_TEST_CONNECTION` explicitamente para esse cluster. Somente nele foram criados banco, esquema pelas migrations existentes e registros de teste. Nenhuma migration foi aplicada ao banco real. Instância temporária encerrada após os testes; arquivos temporários preservados.

| Comando | Resultado final |
|---|---|
| `dotnet build src/backend/SupportAtos.slnx --nologo --no-restore -v minimal` | Sucesso, 0 erros e 0 avisos; SDK instalado é preview e emite mensagem informativa NETSDK1057. |
| `dotnet test src/backend/SupportAtos.slnx --nologo -v minimal --logger 'trx;LogFileName=auth-audit.trx'` | 98 testes de aplicação + 80 de integração aprovados; 0 falhas, 0 ignorados. |
| `dotnet test src/backend/tests/SupportAtos.Qa.Tests/SupportAtos.Qa.Tests.csproj --nologo -v minimal` | 33 falhas por infraestrutura: Docker Engine indisponível. Os requisitos desses cenários ficaram inconclusivos; não são 33 vulnerabilidades comprovadas. |

Foram adicionados 33 casos em `AuthSecurityTests.cs`, além da adaptação dos testes existentes. A cobertura inclui dois hosts independentes com escopos/conexões reais competindo pelo mesmo token; um único sucessor persistido; continuidade do vencedor; reuso tardio com múltiplos descendentes e preservação de login independente; logout concorrente; validade de JWT após logout; desativação da credencial; papel/grupo atuais e negação da política Admin; rollback após falha de assinatura posterior ao SaveChanges; login inexistente chamando Argon2 real com formato válido; atributos de cookie; configurações inseguras; matriz de origens/Referer e preflight CORS no pipeline real.

As primeiras execuções revelaram problemas nos próprios testes novos (decodificação de cookie e enum JSON) e inicialização concorrente preexistente da factory (migrations/seed). Foram corrigidos; os números acima correspondem à execução final. TRX ficam nos diretórios `TestResults` dos projetos, ignorados pelo Git.

## Arquivos alterados nesta auditoria

Caminhos relativos a `src/backend`:

- `src/SupportAtos.Api/Authentication/AuthenticationSetup.cs`: claims internas atuais.
- `src/SupportAtos.Api/Authentication/JwtTokenGerador.cs`: descarte do RSA após assinatura, manutenção de recurso.
- `src/SupportAtos.Api/Controllers/AuthController.cs`: acesso anônimo aos endpoints de autenticação.
- `src/SupportAtos.Api/Middleware/CsrfProtectionMiddleware.cs`: validação explícita de origem/Referer.
- `src/SupportAtos.Api/Program.cs`: lista compartilhada CORS/CSRF, login protegido e validação de cookies ao iniciar.
- `src/SupportAtos.Application/Options/AuthSettings.cs`: explicação correta de SameSite.
- `src/SupportAtos.Application/Ports/ISessaoAutenticacaoRepository.cs`: operação transacional no contrato existente.
- `src/SupportAtos.Application/Services/AuthService.cs`: logs, hash sintético, rotação, reuso e logout.
- `src/SupportAtos.Infrastructure/Persistence/SupportAtosDesignTimeDbContextFactory.cs`: documentação/erro honestos.
- `src/SupportAtos.Infrastructure/Repositories/SessaoAutenticacaoRepository.cs`: transação, bloqueio PostgreSQL, recarga e await do AddAsync.
- `src/SupportAtos.Infrastructure/Repositories/UsuarioRepository.cs`: carregamento de credencial.
- `tests/SupportAtos.Application.Tests/AuthServiceTests.cs`: fakes adaptados, reuso tardio explícito e nomes coerentes com garantias reais.
- `tests/SupportAtos.Integration.Tests/AuthSecurityTests.cs`: testes direcionados novos.
- `tests/SupportAtos.Integration.Tests/TestWebAppFactory.cs`: configuração de autenticação/CORS de teste e serialização de migrations/seed entre hosts de teste.
- `tests/SupportAtos.Integration.Tests/TestTokenFactory.cs`: exportação da chave de teste gerada em memória para assinatura interna.

Adicionado também este relatório em `docs/auditoria-autenticacao-backend.md`. Nenhuma dependência foi adicionada.

## Pendências, limites e melhorias opcionais

- Executar novamente a suíte QA separada com Docker Engine disponível; ela exige seu próprio container e não foi adaptada para contornar esse isolamento.
- A concorrência HTTP usa dois hosts no mesmo processo de teste, com conexões distintas; não foram iniciados dois processos de API separados. A garantia entre processos decorre do bloqueio no servidor PostgreSQL, não de sincronização estática da API. Não foram testados failover, múltiplas máquinas ou carga prolongada.
- Não foram testados navegador real, TLS real, proxy reverso, transporte de cookies em topologia cross-site ou configuração de produção. Conferir as origens e o SameSite na implantação.
- O custo de Argon2 para usuário inexistente foi corrigido e exercitado; não foi realizada análise estatística de timing. Contas bloqueadas/inativas ainda têm caminhos mais curtos no serviço existente. Uniformizar esses caminhos é endurecimento adicional contra enumeração, com custo computacional e de rate limiting a avaliar separadamente.
- Logs antigos que já tenham recebido tokens não podem ser saneados por esta mudança. A necessidade de restringir/remover esses registros e revogar credenciais expostas depende de confirmar que o patch chegou a executar em ambiente com credenciais reais; nenhuma operação desse tipo foi feita no banco real.
- O fallback local preexistente da factory de integração permanece. Para repetir esta validação com segurança, fornecer explicitamente conexão de banco descartável; não executar essa suíte contra banco compartilhado/real.
- Reduzir o caminho do cookie, adicionar `Cache-Control: no-store` às respostas de emissão de token e observabilidade de reuso sem segredos são melhorias opcionais; não foram ampliadas nesta correção. A semântica atual de erros de validação de login/refresh (400) foi preservada.

## Referências consultadas

- [PostgreSQL 17 — bloqueios explícitos e advisory transacional](https://www.postgresql.org/docs/17/explicit-locking.html).
- [OWASP — prevenção de CSRF e tratamento de Origin/Referer ausentes](https://github.com/OWASP/CheatSheetSeries/blob/master/cheatsheets/Cross-Site_Request_Forgery_Prevention_Cheat_Sheet.md).
