# SupportAtos API

Backend .NET 10 / ASP.NET Core, EF Core 10 e PostgreSQL 17, organizado em Domain, Application, Infrastructure e Api. Controllers chamam casos de uso; persistência fica em Infrastructure.

O [README principal](../../README.md) é a fonte oficial do setup. A Etapa B fornece `setup.ps1` e `start.ps1` na raiz, com verificações `-CheckOnly`, EF local 10.0.0 e confirmações interativas para alterações de banco. Consulte as condições de preservação de credenciais e volumes antes de executar.

## Ambiente oficial

- Compose: este diretório, `docker-compose.yml`; sobe somente PostgreSQL 17 em `127.0.0.1:${SUPPORTATOS_DB_PORT:-5433}` (porta HOST configurável; interna `5432`).
- Preservar serviço/container `supportatos-db`, projeto Compose e volume `backend_supportatos-db-data` (projeto padrão `backend`). Não executar o Compose legado em `Db.SuporteAtos/`.
- API: `https://localhost:7099`, com `--launch-profile https`. Endereços HTTP anteriores preservados para compatibilidade.
- Frontend: `https://localhost:5173`, chama a API diretamente e exige HTTPS local.
- Autenticação ativa: login próprio JWT RS256/refresh cookie; API keys separadas para integrações. Não usar Admin mock ou SQL histórico no onboarding.

## EF Core e conexão

A factory pertence à API e usa seus provedores padrão, sem iniciar host/seeders ou abrir conexão. Precedência: argumentos > ambiente > User Secrets em Development > appsettings do ambiente > appsettings base. `ConnectionStrings__DefaultConnection` continua compatível e sobrescreve User Secrets.

Development aceita exclusivamente o Docker oficial (`127.0.0.1:<SUPPORTATOS_DB_PORT>/supportatos`, padrão `5433`, usuário `admin`); API, EF e bootstrap validam esse destino antes de abrir conexão. A porta vem de `.supportatos.local.env` na raiz (ignorado pelo Git), carregado por `setup.ps1`/`start.ps1`; comandos manuais do Compose usam `--env-file ../../.supportatos.local.env`. PostgreSQL Windows é preservado e não é alternativa para este projeto. Na raiz, `setup.ps1 -CheckOnly` diagnostica fontes antigas sem alterá-las; `setup.ps1 -ConnectionOnly` permite a troca autorizada com backup DPAPI/ACL, validação do cluster e sem migrations/bootstrap. Os wrappers aplicam a mesma conexão verificada aos filhos e restauram o ambiente anterior ao terminar; variáveis permanentes não são modificadas. Veja detalhes no README principal.

EF não lê launch profiles. Para usar os User Secrets da API, indicar Development. Comandos a partir de `src/backend`:

```powershell
# Sem acesso ao banco; saída contém metadados do destino.
dotnet ef dbcontext info --project src/SupportAtos.Infrastructure --startup-project src/SupportAtos.Api -- --environment Development

# ALTERA schema: somente na preparação autorizada, após conferir destino/backup.
dotnet ef database update --project src/SupportAtos.Infrastructure --startup-project src/SupportAtos.Api -- --environment Development
```

Migrations permanecem em Infrastructure e são a única fonte do schema. Não aplicar SQL legado em paralelo. A API não aplica migrations automaticamente; o seeder Development cria apenas grupos quando a tabela está vazia. Seed funcional de SLA ainda é pendência.

## Rotina após preparação

Com banco, schema, conexão, JWT e certificados preparados, a partir deste diretório:

```powershell
dotnet run --project src/SupportAtos.Api --launch-profile https
```

Swagger: `https://localhost:7099/swagger`. `/health` verifica somente o host. Não encerrar todos os processos dotnet para liberar uma porta; identificar a instância específica.

Primeiro Admin: usar o comando interativo de [autenticação — fase 3](../../docs/autenticacao-fase3.md) após migrations autorizadas. Preservar credenciais e marcador existentes.

## Testes sem banco

```powershell
dotnet build SupportAtos.slnx
dotnet test tests/SupportAtos.Application.Tests
dotnet test tests/SupportAtos.Integration.Tests --filter FullyQualifiedName~DesignTimeConfigurationTests
```

A suíte completa de integração acessa PostgreSQL/grava dados: usar banco descartável explícito. A [suíte QA](tests/SupportAtos.Qa.Tests/README.md) usa isolamento Docker próprio e recusa overrides de conexão.

Senhas, connection strings reais, API keys, chaves privadas e certificados ficam fora do Git. Não regenerar chaves/certificados na rotina. Ver também [auditoria de autenticação](../../docs/auditoria-autenticacao-backend.md) e [API](../../docs/api.md); relatos anteriores podem descrever configurações históricas.
