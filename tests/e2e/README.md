# Testes E2E — Playwright

Suite end-to-end do portal de suporte, cobrindo o ciclo de vida de um chamado:

**abrir → atribuir → responder → fechar**

O roteiro autentica como agente, abre o chamado `AC-2026-00213` (aberto e sem responsável no seed), assume o atendimento, envia uma resposta ao cliente e altera o status para `Fechado`, validando chips de status, toasts e conteúdo da conversa em cada etapa.

## Pré-requisitos

- Node.js 18+
- Chromium do Playwright (`npx playwright install chromium`)

## Como rodar

```powershell
cd tests\e2e
npm ci
npx playwright install chromium

# compila o frontend e sobe o preview local em http://127.0.0.1:4173
npm run test:e2e:local
```

Para rodar contra o ambiente publicado (Netlify), sem gerar build local:

```powershell
$env:E2E_BASE_URL = 'https://cerulean-syrniki-02dc26.netlify.app'
npm run test:e2e
```

No Bash:

```bash
E2E_BASE_URL='https://cerulean-syrniki-02dc26.netlify.app' npm run test:e2e
```

> No ambiente da Netlify o badge flutuante é escondido automaticamente pelos testes para não interceptar cliques nos menus.

## Variáveis de ambiente

| Variável | Padrão | Uso |
| --- | --- | --- |
| `E2E_BASE_URL` | vazio (preview local em `127.0.0.1:4173`) | URL do frontend a testar |
| `E2E_AGENT_EMAIL` | `gabriel.guimaraes@atoscapital.com` | E-mail usado no login de agente |
| `E2E_AGENT_PASSWORD` | `e2e-playwright` | Senha usada no login de agente |
| `E2E_TICKET_ID` | `AC-2026-00213` | Chamado aberto no início do fluxo |

## Estrutura

```text
tests/e2e/
  e2e/
    helpers/app.ts              # login, abertura do chamado e ajustes do ambiente
    ticket-lifecycle.spec.ts    # fluxo abrir → atribuir → responder → fechar
  playwright.config.ts          # reporters, webServer local e browsers
```

## Relatórios

- `playwright-report/index.html` — relatório HTML (`npm run report` para abrir)
- `reports/junit.xml` — resultado em JUnit (consumível por ferramentas de CI)
- `test-results/` — traces, screenshots e vídeos de falhas

## CI

O workflow [`.github/workflows/e2e.yml`](../../.github/workflows/e2e.yml) roda em pushes para `main` e `feature/**`, em pull requests para `main` e manualmente (`workflow_dispatch`).

- Por padrão compila o frontend e roda a suite contra o preview local.
- No disparo manual é possível informar `base_url` para testar um ambiente publicado.
- Publica o relatório HTML, o JUnit e as evidências de falha como artefato `playwright-report`.

## Observações

- O build atual autentica sem validar credenciais reais e mantém os chamados em memória; cada teste começa com o seed da aplicação.
- A abertura de um **novo** chamado depende da API (`POST /api/chamados` + token). Por isso o E2E cobre a abertura de um chamado existente da fila.
