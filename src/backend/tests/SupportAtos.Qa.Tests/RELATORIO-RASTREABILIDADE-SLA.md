# Validação funcional da rastreabilidade contratual do SLA

Projeto SupportAtos — Squad 74. Execução em 12/11/2026, aproximadamente 16:40 (America/Sao_Paulo).

**Resultado: 2 APROVADOS, 3 FALHOU e 0 INCONCLUSIVOS.** Os cinco cenários executaram a API real, com PostgreSQL 17 exclusivo e migrations reais. As três falhas são de preservação do histórico persistido; não houve falha de infraestrutura ou erro de implementação do teste identificado. Não foram alteradas expectativas para obter aprovação.

## Base normativa e critério de avaliação

O [Termo de Referência v3 original](../../../../Especificacao%20do%20desafio/Termo_Referencia_Residencia_Software.v3.docx), **§5, p. 5**, exige: “Registro de logs de auditoria para rastreabilidade de SLA contratual.” Página conforme paginação salva no DOCX, consultado novamente nesta etapa.

O Termo não impõe tabela, nome de campo ou formato de log. Os cinco cenários solicitados operacionalizam a rastreabilidade como capacidade de recuperar identidade, datas, prazos e eventos anteriores após operações subsequentes. Avaliou-se também a reconstrução por interações, dados do chamado, versões/eventos persistidos e outras relações existentes; ausência de linhas em `log_auditoria`, isoladamente, não reprova um cenário.

Snapshots anteriores e respostas HTTP são **evidências do teste sobre o que aconteceu**, não histórico disponibilizado pelo produto. Nos cenários de preservação, a recuperação usa somente o último snapshot SQL, depois de novas operações. Capturas de webhook existem apenas no handler de teste e não são contadas como armazenamento durável da aplicação.

## Revisão, isolamento e execução

- Branch confirmada antes da alteração: `feature/danilo-testes-sla-webhook-74`.
- HEAD: `8c2d1e2a29ea144998d64e72192a380bbc5d6c1d`. O arquivo novo `AuditoriaSlaTests.cs` estava sem commit durante a execução; o SHA identifica a base de produção testada.
- Docker inicialmente indisponível; `docker desktop start --timeout 45` iniciou o serviço e `docker info` confirmou Engine **29.9.1** antes dos testes. Esse impedimento inicial não bloqueou nenhum cenário.
- SDK `11.0.100-rc.3.26522.148`, alvo/runtime .NET 10. Compilação pelo `dotnet test` concluída; somente mensagem informativa NETSDK1057 sobre SDK preview.
- Reutilizados `QaPostgres`, `SlaRf06Case` e `SlaRf06Application`, sem modificá-los. Um banco novo por cenário; conexão do host conferida; nenhum fallback de desenvolvimento/produção.
- Autenticação, provisionamento, casos de uso, dispatcher e EF/Npgsql reais. HTTP de saída interceptado pelo handler existente; nenhum webhook foi enviado a serviço real.
- Consultas Npgsql independentes examinaram todas as relações públicas expostas por `information_schema.columns`: `__EFMigrationsHistory`, `anexo`, `chamado`, `grupo_empresa`, `interacao`, `log_auditoria`, `provisionamento_audit`, `sla_config` e `usuario`.
- Metadados de colunas e triggers foram gravados em cada JSON. Nenhum trigger não interno foi encontrado no esquema público desses bancos migrados.
- O runner retornou **código 1**, com TRX total=5, executed=5, passed=2, failed=3. Duração dos testes: 9 segundos. O código não zero é esperado pelas falhas de rastreabilidade e deve ser preservado em CI.
- A fixture confirmou remoção do container exclusivo `99ba25a00851e2584165c74e87ca9c7eec8d93b71c01c48e8fd54170f32bcc59`. Consulta Docker final pelo rótulo da fixture não encontrou remanescentes.

## Resultado dos cinco testes

Todos os critérios abaixo se referem ao objetivo de rastreabilidade do §5; campos específicos são evidência da implementação atual, não uma solução imposta pelo Termo.

| ID | Resultado | Evidência HTTP | Registrado e recuperável no banco | Perdido/não registrado e conclusão |
|---|---|---|---|---|
| AUD-01 — abertura | **APROVADO** | POST de abertura: 201. | Chamado com código público, criação, usuário 61001, grupo 71001 e vencimentos de resposta/resolução de +60/+480 minutos; identidade e interação inicial vinculadas. | `log_auditoria` vazio não impede reconstruir a abertura nesse estado. Aprovação da informação disponível após criar; não demonstra histórico de futuras alterações de prazos/identidade. [JSON](TestResults/auditoria-sla-20261112-a5f1f688/AUD-01.json) |
| AUD-02 — status | **FALHOU** | Abertura 201; Aberto→EmAndamento 200; EmAndamento→AguardandoCliente 200. | Estado final AguardandoCliente e momento da última atualização. | Estado anterior e instante da primeira mudança não são recuperáveis do banco final. Todas as relações foram consultadas; `log_auditoria` permaneceu vazio. [JSON](TestResults/auditoria-sla-20261112-a5f1f688/AUD-02.json) |
| AUD-03 — resposta | **APROVADO** | Abertura 201; resposta pública 201; mudança posterior de status 200. | Interação 2 mantém autor 62001, data e vínculo. Sua data continua disponível depois de `AtualizadoEm` mudar. Prazo de resposta permite comparação e cálculo do tempo. | Não é necessário um campo `PrimeiraRespostaEm` para este cenário: a primeira interação pública da equipe é identificável nos dados testados. Não comprova imutabilidade futura de papel/nome do usuário ou de configuração. [JSON](TestResults/auditoria-sla-20261112-a5f1f688/AUD-03.json) |
| AUD-04 — resolução/fechamento | **FALHOU** | Abertura 201; Resolvido 200; Fechado 200. | Estado final Fechado e instante de fechamento em `FechadoEm`. | O instante anterior de resolução, antes visível em `AtualizadoEm`, é sobrescrito ao fechar; não foi preservado em outra relação. Não se consegue separar os dois marcos pelo banco final. [JSON](TestResults/auditoria-sla-20261112-a5f1f688/AUD-04.json) |
| AUD-05 — reabertura | **FALHOU** | Abertura 201; resposta 201; resolver, fechar, reabrir e voltar a EmAndamento: quatro respostas 200. | Criação, vencimentos, identidade e as duas interações permanecem disponíveis e iguais aos dados anteriores. | `FechadoEm` fica nulo; `AtualizadoEm` passa a representar a última mudança. Resolução e fechamento anteriores não são recuperáveis por outra fonte persistida. [JSON](TestResults/auditoria-sla-20261112-a5f1f688/AUD-05.json) |

## Evidências SQL e distinção entre dado atual e histórico

Datas abaixo em UTC, conforme convenção do backend para timestamps sem fuso. Os JSON contêm SQL executado, payloads/respostas HTTP, schema, capturas de webhook sem chaves e verificações individuais.

| Cenário | Observação anterior feita pelo teste | Estado final efetivamente recuperável |
|---|---|---|
| AUD-02 | EmAndamento em `AtualizadoEm=2026-11-12T19:40:11.805460`. | AguardandoCliente, `AtualizadoEm=2026-11-12T19:40:11.883031`; uma interação inicial, nenhuma auditoria do chamado. O timestamp anterior só continua existindo no artefato do teste. |
| AUD-03 | Primeira resposta pública da equipe em `2026-11-12T19:40:09.059609`, autor 62001. | Mesma interação e data preservadas após outra mudança. Prazo `2026-11-12T20:40:08.895184`; resposta 3599,835575 segundos antes do vencimento. A aprovação verifica capacidade de apuração, sem exigir que todo atendimento cumpra SLA. |
| AUD-04 | Resolvido em `AtualizadoEm=2026-11-12T19:40:07.433393`. | Fechado, `AtualizadoEm=FechadoEm=2026-11-12T19:40:07.556959`; sem marco anterior persistido da resolução. |
| AUD-05 | Antes da reabertura, `FechadoEm=2026-11-12T19:40:05.692130`. | Após reabertura e nova mudança, `FechadoEm=null` e `AtualizadoEm=2026-11-12T19:40:05.836226`. As duas interações permanecem, mas não descrevem resolução/fechamento. |

`log_auditoria` apresentou **zero registros nos cinco cenários**, apesar das operações concluídas. `provisionamento_audit` registrou uma linha no AUD-01 e duas nos demais cenários, referentes ao cliente/agente: essas linhas não contêm as mudanças de estado do chamado. Consultar essa fonte não recuperou os eventos perdidos.

As buscas de reconstrução aceitam eventos com anterior/novo, versões datadas vinculadas ao chamado ou marcos temporais no próprio registro. Não exigem exclusivamente `LogAuditoria` ou `ResolvidoEm`. Os artefatos completos permitem inspeção independente das buscas automatizadas; nas outras tabelas consultadas não havia eventos alternativos de status/resolução/fechamento.

Evidências gerais: [TRX](TestResults/auditoria-sla-20261112-a5f1f688/auditoria-sla.trx), [log de execução](TestResults/auditoria-sla-20261112-a5f1f688/execucao.log), [registro de descarte da fixture](TestResults/auditoria-sla-20261112-a5f1f688/RESUMO.md). O `RESUMO.md` da fixture mantém o catálogo antigo AUTH/RF01/RF10; resultados AUD estão nos cinco JSON e no TRX. `TestResults/` é ignorado pelo Git: preservar/publicar esses artefatos para revisão e CI.

## Lacunas confirmadas e Issue 4

**Existe fundamento funcional suficiente para abrir a Issue 4, com escopo de preservação do histórico contratual no backend versionado.** Não é apenas ausência de uma tabela preenchida: operações HTTP bem-sucedidas sobrescreveram marcos temporais e o estado anterior não permaneceu recuperável nas fontes persistidas examinadas.

Título sugerido: **Preservar histórico contratual de status, resolução e fechamento do SLA após alterações e reabertura**. Prioridade sugerida: **alta**.

Evidências centrais para a issue: AUD-02, AUD-04 e AUD-05. Manter AUD-01/AUD-03 aprovados como contraponto: criação, identidade, prazos e respostas já fornecem rastreabilidade parcial. Não justificar a issue simplesmente por falta de `PrimeiraRespostaEm` ou por `log_auditoria` vazio.

**Limite da conclusão:** não foram acessados portal, n8n, sinks de logging ou bancos externos. A factory existente substitui providers de log por coletor de erros SQL; este ensaio não comprova ausência de auditoria em serviços externos ou logs operacionais implantados. Se a equipe sustentar uma fonte externa durável, solicitar evidência de reconstrução dos mesmos eventos antes de afirmar descumprimento do sistema completo. Capturar o webhook no teste não comprova que essa fonte externa existe ou preserva o contrato.

Não foram avaliados RF09, medição automática, alertas, alterações de configuração de SLA, concorrência ou todo o ciclo de estados. Aprovar dois cenários não implica conformidade completa do §5.

## Reprodução

Executar da raiz, na branch indicada, com Docker Engine Linux local disponível e sem overrides de conexão (`SUPPORTATOS_TEST_CONNECTION`, `QA_CONNECTION_STRING`, `ConnectionStrings__DefaultConnection`). A fixture recusa esses overrides; não apontar a suíte para bancos existentes.

```powershell
git branch --show-current
docker info --format '{{.ServerVersion}}'
# Se necessário, foi utilizado nesta execução: docker desktop start --timeout 45

$auditRoot = Join-Path (Get-Location) ('src/backend/tests/SupportAtos.Qa.Tests/TestResults/auditoria-sla-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $auditRoot | Out-Null
$previousReport = $env:QA_REPORT_DIRECTORY
$env:QA_REPORT_DIRECTORY = $auditRoot
try {
    dotnet test src/backend/tests/SupportAtos.Qa.Tests/SupportAtos.Qa.Tests.csproj --filter 'FullyQualifiedName~AuditoriaSlaTests' --logger 'trx;LogFileName=auditoria-sla.trx' --results-directory $auditRoot --nologo *> (Join-Path $auditRoot 'execucao.log')
    $auditExit = $LASTEXITCODE
} finally { $env:QA_REPORT_DIRECTORY = $previousReport }
Write-Output "Exit code: $auditExit; evidências: $auditRoot"
# Em CI: exit $auditExit (não ocultar as falhas de rastreabilidade).
```

Arquivos criados: [AuditoriaSlaTests.cs](AuditoriaSlaTests.cs) e este relatório. Nenhum código de produção, migration ou teste anterior foi alterado. Nenhum commit ou PR foi criado.
