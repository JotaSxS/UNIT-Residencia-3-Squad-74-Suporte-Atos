# Autenticação própria — Fase 3

Implementação e validação em 19/11/2026. Reutiliza `Usuario`, `UsuarioCredencial`, `CadastrarUsuarioUseCase`, repositórios, Unit of Work, Argon2id, geração aleatória e autenticação JWT existentes. Nenhuma dependência adicionada. Frontend, API Key, provisionamento do portal, chamados e grupos empresariais não foram modificados.

## Bootstrap explícito do primeiro Admin

Depois de aplicar as migrations por procedimento autorizado, configurar `ConnectionStrings:DefaultConnection` por user-secrets da API ou `ConnectionStrings__DefaultConnection` no ambiente do processo. Não colocar credenciais na linha de comando nem no repositório.

Em terminal interativo, na raiz do repositório:

```powershell
dotnet run --no-launch-profile --project src/backend/src/SupportAtos.Api -- --bootstrap-admin
```

Em implantação publicada, usar o executável/DLL da API com a mesma flag. O procedimento:

1. Recusa entrada/saída redirecionadas e argumentos adicionais.
2. Mostra somente host, porta e banco de destino, sem connection string ou senha.
3. Solicita nome e e-mail reais, senha sem eco e confirmação da senha. Exige entre 15 e 128 caracteres; sem senha padrão ou composição obrigatória.
4. Solicita confirmação literal `INICIALIZAR <nome do banco>`.
5. Abre transação `ReadCommitted`, obtém `pg_advisory_xact_lock(74102, 0)` e recusa se já existir qualquer Admin ou o marcador de bootstrap.
6. Cria um novo usuário Admin sem grupo e a credencial Argon2id da senha definida pelo próprio operador nesse procedimento. Ao confirmar, a credencial já pode fazer login porque a primeira senha acabou de ser definida.
7. Persiste o marcador único na mesma transação. Falhas fazem rollback de usuário, credencial e marcador. Excluir a conta posteriormente não reabre o bootstrap.

Não existe endpoint HTTP de bootstrap. O comando não inicia o servidor/hosted services, não executa seeders e não aplica migrations. O seeder preexistente de grupos no host normal de desenvolvimento não foi alterado. Quem executa o comando precisa ter acesso administrativo ao ambiente e às credenciais do PostgreSQL. Não apagar o marcador para recuperar contas; recuperação/redefinição de senha é outro fluxo.

## Cadastro de funcionários

Endpoint existente: **`POST /api/usuarios`**, com JWT de Admin. A política HTTP existente continua exclusiva de Admin e o serviço também verifica o papel do chamador. API Key, Supervisor, Agente e anônimo não recebem esse acesso.

Campos de entrada existentes:

```json
{
  "nome": "<nome real>",
  "email": "<email real>",
  "grupoEmpresaId": null,
  "papel": "Agente"
}
```

Para `Admin`, `Supervisor` e `Agente`, a resposta 201 preserva `id`, `nome`, `email`, `grupoEmpresaId`, `grupoEmpresaNome` e `papel` na raiz e acrescenta `tokenAtivacao` e `expiraEm`. Retorna `Cache-Control: no-store`. Os campos originais continuam desserializáveis como `UsuarioDto`.

A transação cria usuário e credencial **inativa**, com senha vazia (não existe senha provisória). Gera token de 32 bytes criptograficamente aleatórios com o gerador seguro existente; expiração de **24 horas**, em UTC. Somente SHA-256 do token é persistido. A resposta de cadastro é a única oportunidade de obter o token original; consultas posteriores de usuários não o retornam. O índice único de e-mail existente evita duplicatas; concorrência resulta em um cadastro e um conflito 409, sem credencial órfã.

`Cliente` conserva o fluxo anterior de cadastro com grupo obrigatório, sem criar credencial ou convite. Papéis desconhecidos são rejeitados pelo serviço de funcionários. E-mail já existente, inclusive de cliente/portal, gera conflito: não promove, converte ou associa credencial ao usuário existente.

Não há provedor de e-mail conectado nesta etapa. O Admin deve entregar o convite ao funcionário por canal seguro, usando HTTPS no acesso à API. Não colocar token em query string, logs, chamados ou mensagens de diagnóstico. O token é um segredo bearer: quem o possui pode escolher a primeira senha enquanto ele for válido.

## Ativação e primeira senha

Novo endpoint anônimo: **`POST /api/auth/ativar`**. Enviar JSON com `token` recebido e `senha` escolhida pelo funcionário (15 a 128 caracteres). Não usar token na URL. A proteção CSRF existente também cobre essa rota: informar `Origin` permitido ou `Referer` de origem permitida. Ausência de ambos é 403.

O serviço calcula o hash do token, identifica a credencial, adquire o bloqueio transacional por usuário `pg_advisory_xact_lock(74101, usuarioId)` e bloqueia a linha de usuário com `FOR UPDATE`. Após esperar, relê o estado de usuário e credencial. Exige:

- Credencial ainda inativa e convite não consumido, não expirado.
- Papel atual Admin, Supervisor ou Agente.
- Usuário sem código externo de portal.

A primeira senha recebe Argon2id pela implementação existente. Senha, ativação da credencial e consumo do convite são gravados na mesma transação. Apenas uma ativação concorrente tem sucesso; a outra recebe 400. Não são emitidos JWT/cookie na ativação: após o 204, o funcionário usa o login existente.

Convite desconhecido, expirado, já utilizado ou usuário incompatível retorna erro genérico 400. Uma tentativa com senha fora da política não consome o convite. O hash e as datas ficam como evidência de consumo, sem guardar o token original. Repetir convite consumido não altera senha e não reativa credencial posteriormente desativada. `UsuarioCredencial.Reativar` também recusa credencial sem primeira senha.

Nenhum logger novo registra senha, token ou hash de convite. O comando usa mensagens fixas para erros e desabilita provedores de logging do container de serviços criado para o bootstrap. Não foi habilitado logging sensível do EF.

## Migration necessária

`20261119205912_AdicionarBootstrapEAtivacaoFuncionarios`:

- Colunas opcionais `token_ativacao_hash` (`varchar(64)`), `token_ativacao_expira_em` e `ativacao_consumida_em` em `usuario_credencial`.
- Índice único parcial para hashes não nulos.
- Tabela `autenticacao_bootstrap`, com PK fixa 1, check `id = 1` e data de execução. Sem FK para o Admin, para preservar o marcador mesmo após excluir a conta.
- Atualização do snapshot e Designer gerados pelo EF.

Não há seed, atualização de senhas, criação de contas ou alteração de registros existentes na migration. As credenciais existentes continuam com os valores anteriores. O comando de bootstrap depende do esquema atualizado e não tenta atualizá-lo.

**A migration foi aplicada somente nos bancos temporários de teste. Não foi executada no banco real.** A aplicação em ambiente real permanece pendente de autorização. Para ferramentas EF, a factory existente exige `ConnectionStrings__DefaultConnection`; ela não carrega user-secrets, conforme documentado na auditoria anterior. Downgrade remove informações de convite e o marcador: não executar como mecanismo de recuperação.

## Validação executada

PostgreSQL 18 em cluster novo, diretório exclusivo no TEMP, porta `127.0.0.1:55479`, usuário exclusivo da execução. `SUPPORTATOS_TEST_CONNECTION` foi informado explicitamente à suíte. Os testes de bootstrap criam bancos adicionais com nomes exclusivos dentro desse mesmo cluster. Contas e senhas sintéticas existem somente nesses bancos de teste, sem afetar dados reais. A instância foi encerrada ao terminar; diretório temporário preservado.

| Verificação | Resultado |
|---|---|
| `dotnet build src/backend/SupportAtos.slnx --nologo --no-restore -v minimal` | Sucesso, 0 erros e 0 avisos; mensagem informativa de SDK preview instalado. |
| `dotnet test src/backend/SupportAtos.slnx --nologo -v minimal --logger 'trx;LogFileName=auth-phase3.trx'` | **102 testes de aplicação e 103 de integração aprovados**, 0 falhas/ignorados na execução final. |
| `dotnet ef migrations has-pending-model-changes --project src/backend/src/SupportAtos.Infrastructure --startup-project src/backend/src/SupportAtos.Api` | Nenhuma mudança pendente após a migration. Usada configuração de design sem banco acessível, sem aplicar migration. |
| Bootstrap via processo sem terminal/redirecionado | Recusado, exit code 1; sem acesso ao banco. |
| Suíte separada `SupportAtos.Qa.Tests` | 33 cenários falharam por infraestrutura: Docker Engine indisponível. Cenários inconclusivos, não falhas funcionais comprovadas. |

Foram adicionados **27 casos**: 4 de domínio e 23 de integração. Cobrem os três papéis internos; credencial inativa e ausência de senha provisória; hash do convite; Argon2 real e login após ativação; preservação do formato original de cadastro e não exposição do convite em consultas; autorização; clientes/portal preservados; expiração; reutilização; senha inválida sem consumo; CSRF; concorrência de ativação entre dois hosts; cadastros simultâneos; bootstrap simultâneo, repetido e após exclusão do Admin; rollback real após falha posterior ao SaveChanges, tanto no bootstrap quanto no cadastro.

Durante a revisão do contrato HTTP, um teste novo tentou desserializar duas vezes o stream já fechado da resposta; o helper foi corrigido para ler uma vez e desserializar o JSON em memória. A execução final acima inclui essa correção. Evidências TRX estão em `TestResults` dos projetos, ignoradas pelo Git.

## Arquivos desta fase

Novos, relativos a `src/backend`:

- `src/SupportAtos.Api/Authentication/BootstrapAdminCommand.cs`.
- `src/SupportAtos.Application/DTOs/AtivacaoFuncionarioDto.cs`.
- `src/SupportAtos.Application/Ports/IFuncionarioAutenticacaoRepository.cs`.
- `src/SupportAtos.Application/Services/FuncionarioAutenticacaoService.cs`.
- `src/SupportAtos.Domain/Entities/AutenticacaoBootstrap.cs`.
- `src/SupportAtos.Infrastructure/Repositories/FuncionarioAutenticacaoRepository.cs`.
- `src/SupportAtos.Infrastructure/Persistence/Configurations/AutenticacaoBootstrapConfiguration.cs`.
- Migration e Designer `20261119205912_AdicionarBootstrapEAtivacaoFuncionarios`.
- `tests/SupportAtos.Application.Tests/AtivacaoCredencialTests.cs`.
- `tests/SupportAtos.Integration.Tests/BootstrapAdminTests.cs`.
- `tests/SupportAtos.Integration.Tests/FuncionariosAtivacaoTests.cs`.

Alterados: `Program.cs`, `AuthController.cs`, `UsuariosController.cs`, injeção de dependências em Application/Infrastructure, `UsuarioCredencial.cs`, sua configuração EF e `SupportAtosDbContextModelSnapshot.cs`. Adicionado também este documento em `docs/autenticacao-fase3.md`. As mudanças preexistentes e a auditoria anterior foram preservadas. Sem commit ou push.

## Pendências e limites

- Aplicação da migration e bootstrap real aguardam procedimento autorizado do operador. Nenhum Admin real foi criado nesta execução.
- Entrega automática de convites, interface de ativação, reemissão de convite perdido/expirado e recuperação de senha não foram incluídas. Convite expirado permanece inativo; não editar hashes no banco como substituto de um futuro fluxo de reemissão.
- A interface interativa do console (leitura de teclado/mascaramento) não foi automatizada de ponta a ponta. A recusa sem terminal foi exercitada por processo, e a lógica transacional do bootstrap foi testada com PostgreSQL real.
- Concorrência usa hosts/escopos/conexões distintos no mesmo processo de testes. O bloqueio é executado no PostgreSQL e vale para processos que usem o mesmo protocolo; não foram testados dois processos HTTP, failover, múltiplas máquinas ou carga prolongada.
- Não foram testados navegador, TLS/proxy reais, serviço de e-mail ou entrega externa. Não adicionar captura de corpos HTTP contendo senhas/convites ao proxy/APM sem redigir esses campos.
- A suíte QA separada precisa ser repetida com Docker Engine disponível. Os testes existentes de integração conservam fallback local; em novas execuções, definir explicitamente uma conexão descartável e nunca usar banco real/compartilhado.

Referências: [OWASP — tokens aleatórios, expiração e uso único](https://cheatsheetseries.owasp.org/cheatsheets/Forgot_Password_Cheat_Sheet.html) e [PostgreSQL 18 — bloqueios transacionais](https://www.postgresql.org/docs/18/explicit-locking.html).
