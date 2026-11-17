# Testes de integração — SLA (RF07) e Webhooks (RF06)

**Projeto:** SupportAtos — Squad 74  
**Branch:** `feature/danilo-testes-sla-webhook-74`  
**Data da validação:** 12/11/2026  
**Base testada:** `3fff127442d724c7556e05d2fb191c9d7028ff29` (testes novos ainda não commitados nessa base)

## Objetivo e abordagem

Foram implementados **10 testes de integração automatizados** com xUnit, executando a API ASP.NET Core e os casos de uso reais, migrations reais e PostgreSQL 17 descartável no Docker, com banco isolado por cenário. As verificações combinam respostas HTTP, consultas SQL independentes e captura de webhooks. O dispatcher de webhooks é real, mas o transporte HTTP externo é interceptado por um handler de teste; nenhuma notificação foi enviada ao portal real.

## Cenários executados

| ID | O que o teste verificou | Resultado |
|---|---|---|
| **SLA-01** | Abertura com SLA configurado: HTTP 201, prioridade correta e vencimentos de resposta (**+60 min**) e resolução (**+480 min**) persistidos. | **Aprovado** |
| **SLA-02** | Abertura sem SLA configurado: HTTP 400, sem chamado/interação e sem webhook. | **Aprovado** |
| **SLA-03** | Mudança para `EmAndamento`: estado atualizado no banco e prazos originais preservados. | **Aprovado** |
| **SLA-04** | Entrada e saída de `AguardandoCliente`: campo `AguardandoDesde` preenchido e depois limpo, preservando os prazos. | **Aprovado** |
| **RF06-01** | Criação do chamado emite `chamado.criado` com código e IDs correspondentes ao banco. | **Aprovado** |
| **RF06-02** | Interação pública emite `chamado.atualizado`; interação e estado ficam persistidos. | **Aprovado** |
| **RF06-03** | Mudança para `AguardandoCliente` emite `chamado.aguardando_cliente`, com status persistido. | **Aprovado** |
| **RF06-04** | Fechamento emite `chamado.fechado`, com status e data de fechamento persistidos. | **Aprovado** |
| **RF06-05** | Validação do POST JSON: nove campos esperados, dados coerentes com o banco e header `X-Api-Key` correto, sem expor segredo. | **Aprovado** |
| **RF06-06** | Falha transitória simulada (HTTP 500 → 500 → 202): três tentativas com mesmo payload, sem duplicar chamado ou interação. | **Aprovado** |

## Resultado consolidado

| Conjunto | Aprovados | Falhas | Bloqueios |
|---|---:|---:|---:|
| **Novos testes SLA/RF06** | **10/10** | 0 | 0 |
| **Regressão QA anterior (AUTH/RF01/RF10)** | **18/18** | 0 | 0 |
| **Total executado nesta reexecução** | **28/28** | **0** | **0** |

Os testes de aplicação (**44/44**) e de segurança/sessão (**22/22**) também haviam passado na execução anterior, **não tendo sido reexecutados nesta etapa**. Os 28 testes acima foram executados funcionalmente com Docker disponível, com saída de código `0`.

## Observação identificada

**SLA-02 — provisionamento parcial:** embora a abertura sem SLA seja corretamente rejeitada (HTTP 400), o fluxo já havia cadastrado **1 usuário, 1 grupo empresarial e 1 registro de auditoria de provisionamento**. Não houve criação de chamado, interação ou envio de webhook. É um comportamento a avaliar quanto à atomicidade, **não uma falha do cenário definido**.

## Escopo e limitações

A cobertura foi de **100% dos 10 cenários planejados**, **não** de todo o RF06/RF07 ou do código-fonte. Não foram testados monitoramento e alertas de vencimento (**RF09**), medição efetiva de cumprimento do SLA, timeouts/cancelamento, entrega durável, deduplicação ou transporte real pela rede (DNS/TLS/proxy). Os webhooks foram capturados em ambiente controlado.

## Arquivos entregues

- `src/backend/tests/SupportAtos.Qa.Tests/SlaRf06Application.cs` — infraestrutura de execução isolada e captura de webhooks.
- `src/backend/tests/SupportAtos.Qa.Tests/SlaRf06Tests.cs` — 10 testes xUnit.
- Este relatório resumido — cobertura e resultados para revisão do PR.

**Conclusão:** os 10 cenários novos e os 18 de regressão passaram, sem alterações no código de produção ou nas migrations. A integração SLA/RF06 está validada **nos fluxos e limites explicitamente testados**.
