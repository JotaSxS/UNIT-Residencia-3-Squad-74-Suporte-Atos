# Shared by setup.ps1/start.ps1. Compatible with Windows PowerShell 5.1 and PowerShell 7.
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$script:RepoRoot = Split-Path -Parent $PSScriptRoot
$script:HelperProject = Join-Path $script:RepoRoot 'src/backend/tools/SupportAtos.DevSetup/SupportAtos.DevSetup.csproj'
$script:HelperDll = Join-Path $script:RepoRoot 'src/backend/tools/SupportAtos.DevSetup/bin/Release/net10.0/SupportAtos.DevSetup.dll'
$script:DefaultSupportAtosDbPort = 5433

function Get-SupportAtosDbPort {
    $raw = $env:SUPPORTATOS_DB_PORT
    if ([string]::IsNullOrWhiteSpace($raw)) { return $script:DefaultSupportAtosDbPort }
    $port = 0
    if (-not [int]::TryParse($raw.Trim(), [ref]$port) -or $port -lt 1 -or $port -gt 65535) {
        throw "SUPPORTATOS_DB_PORT invalida. Use um numero entre 1 e 65535 em .supportatos.local.env."
    }
    return $port
}

function Initialize-SupportAtosDbPort {
    # .supportatos.local.env e a unica fonte local da porta HOST. O Compose recebe o
    # mesmo valor pelo ambiente do processo; nao dependemos de .env auto-carregado.
    $localFile = Join-Path $script:RepoRoot '.supportatos.local.env'
    if (Test-Path -LiteralPath $localFile) {
        foreach ($line in [System.IO.File]::ReadAllLines($localFile)) {
            $text = $line.Trim().TrimStart([char]0xFEFF).Trim()
            if ($text.Length -eq 0 -or $text.StartsWith('#')) { continue }
            $pair = $text.Split('=', 2)
            if ($pair.Count -ne 2) { throw 'Linha invalida em .supportatos.local.env. Use SUPPORTATOS_DB_PORT=<porta>.' }
            if ($pair[0].Trim() -ne 'SUPPORTATOS_DB_PORT') { continue }
            $filePort = $pair[1].Trim().Trim('"').Trim("'")
            $environmentPort = $env:SUPPORTATOS_DB_PORT
            if (-not [string]::IsNullOrWhiteSpace($environmentPort) -and $environmentPort.Trim() -ne $filePort) {
                throw "SUPPORTATOS_DB_PORT=$($environmentPort.Trim()) difere de .supportatos.local.env ($filePort). Use uma unica fonte local para a porta."
            }
            $env:SUPPORTATOS_DB_PORT = $filePort
        }
    }
    $null = Get-SupportAtosDbPort
}

. (Join-Path $PSScriptRoot 'Development.Connection.ps1')

function Invoke-Checked {
    param([string]$Command, [string[]]$Arguments, [string]$Failure)
    & $Command @Arguments
    if ($LASTEXITCODE -ne 0) { throw $Failure }
}

function Get-DotnetSdkSupport {
    param([string]$VersionText)
    $selected = $VersionText.Trim()
    if ($selected -notmatch '^(\d+)\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?(?:\+[0-9A-Za-z.-]+)?$' -or [int]$Matches[1] -lt 10) {
        throw 'O SDK selecionado deve ser .NET 10 ou superior. Confira dotnet --version, PATH e eventual global.json; runtime sozinho nao basta.'
    }
    [pscustomobject]@{ Version = $selected; IsPreview = $selected.Contains('-') }
}

function Test-DotnetCompatibility {
    # --version checks the SDK actually selected in this checkout, not just installed SDKs.
    $selected = & dotnet --version
    if ($LASTEXITCODE -ne 0) { throw 'Nao foi possivel selecionar um SDK. Confira PATH e eventual global.json.' }
    $sdk = Get-DotnetSdkSupport ([string]$selected)
    if ($sdk.IsPreview) {
        Write-Host "AVISO: SDK $($sdk.Version) Preview selecionado; permitido. Recomendacao: .NET 10 estavel." -ForegroundColor Yellow
    }
    $runtimes = @(& dotnet --list-runtimes)
    if ($LASTEXITCODE -ne 0 -or -not ($runtimes -match '^Microsoft.AspNetCore.App 10\.0\.\d+ ') -or
        -not ($runtimes -match '^Microsoft.NETCore.App 10\.0\.\d+ ')) {
        throw 'Instale os runtimes .NET/ASP.NET Core 10 para executar net10.0 e o EF fixado. Um SDK mais novo nao substitui esses runtimes.'
    }
    # Evaluate the real project/imports without restore, compilation, host or database.
    $project = Join-Path $script:RepoRoot 'src/backend/src/SupportAtos.Api/SupportAtos.Api.csproj'
    $evaluation = & dotnet msbuild $project -nologo -getProperty:TargetFramework -getProperty:NETCoreSdkVersion
    if ($LASTEXITCODE -ne 0) { throw 'O SDK nao conseguiu avaliar o projeto. Confira SDK, imports e global.json antes de continuar.' }
    $properties = (($evaluation -join "`n") | ConvertFrom-Json).Properties
    if ($properties.TargetFramework -ne 'net10.0' -or $properties.NETCoreSdkVersion -ne $sdk.Version) {
        throw 'Avaliacao do projeto divergiu do SDK/target esperado. Revise a configuracao antes de continuar.'
    }
    Write-Host "SDK $($sdk.Version): projeto net10.0 avaliado e runtimes 10 disponiveis." -ForegroundColor Green
    $previousErrorAction = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        $ef = @(& dotnet tool run dotnet-ef -- --version 2>&1)
        $efExit = $LASTEXITCODE
    } finally { $ErrorActionPreference = $previousErrorAction }
    if ($efExit -eq 0) {
        $manifest = Get-Content -LiteralPath (Join-Path $script:RepoRoot '.config/dotnet-tools.json') -Raw | ConvertFrom-Json
        $pinned = $manifest.tools.'dotnet-ef'.version
        if (($ef -join "`n") -notmatch ("(?m)^" + [regex]::Escape($pinned) + '\s*$')) {
            throw 'A versao executada do EF diverge do manifesto local. Execute dotnet tool restore na raiz.'
        }
        Write-Host "EF local $pinned executavel: OK." -ForegroundColor Green
    } else {
        Write-Host 'AVISO: EF local ainda nao executavel. O setup completo restaura e valida a ferramenta antes de preparar recursos; CheckOnly nao instala ferramentas.' -ForegroundColor Yellow
    }
}

function Test-DevelopmentPrerequisites {
    Initialize-SupportAtosDbPort
    $failures = [System.Collections.Generic.List[string]]::new()
    if ($env:OS -ne 'Windows_NT') { $failures.Add('Use Windows/PowerShell para este setup.') }
    foreach ($name in @('git', 'docker', 'dotnet', 'node', 'npm')) {
        if (-not (Get-Command $name -ErrorAction SilentlyContinue)) { $failures.Add("Instale $name e reabra o terminal.") }
    }
    if (Get-Command git -ErrorAction SilentlyContinue) {
        & git -C $script:RepoRoot rev-parse --is-inside-work-tree 2>$null | Out-Null
        if ($LASTEXITCODE -ne 0) { $failures.Add('Execute em um clone Git valido do SupportAtos.') }
    }
    if (Get-Command dotnet -ErrorAction SilentlyContinue) {
        try { Test-DotnetCompatibility }
        catch { $failures.Add($_.Exception.Message) }
    }
    if (Get-Command node -ErrorAction SilentlyContinue) {
        $version = [string](& node --version)
        if ($LASTEXITCODE -ne 0 -or $version -notmatch '^v(\d+)\.' -or [int]$Matches[1] -lt 22) { $failures.Add('Instale Node.js 22 LTS ou mais recente compativel; recomendado 22.') }
    }
    if (Get-Command npm -ErrorAction SilentlyContinue) {
        & npm --version | Out-Null
        if ($LASTEXITCODE -ne 0) { $failures.Add('Corrija a instalacao do npm e sua politica de execucao PowerShell.') }
    }
    if (Get-Command docker -ErrorAction SilentlyContinue) {
        & docker compose version 2>$null | Out-Null
        if ($LASTEXITCODE -ne 0) { $failures.Add('Instale/atualize Docker Compose v2 pelo Docker Desktop.') }
        $engine = & docker info --format '{{.OSType}}' 2>$null
        if ($LASTEXITCODE -ne 0 -or $engine -ne 'linux') { $failures.Add('Inicie Docker Desktop e selecione containers Linux.') }
        $endpoint = $null
        if (-not $env:DOCKER_CONTEXT) { $endpoint = $env:DOCKER_HOST }
        if (-not $endpoint) { $endpoint = [string](& docker context inspect --format '{{.Endpoints.docker.Host}}' 2>$null) }
        if ($endpoint -notmatch '^(npipe:////\./pipe/|unix:///|tcp://(localhost|127\.0\.0\.1|\[::1\]):)') {
            $failures.Add('Selecione um contexto Docker local. Este setup nao opera em Docker remoto.')
        }
        & docker compose -f (Join-Path $script:RepoRoot 'src/backend/docker-compose.yml') config --quiet 2>$null
        if ($LASTEXITCODE -ne 0) { $failures.Add('Revise o Compose oficial e os overrides locais; configuracao invalida.') }
        $help = (& docker compose up --help) -join "`n"
        if ($help -notmatch '--wait-timeout' -or $help -notmatch '--no-recreate') { $failures.Add('Atualize Compose v2: necessario --wait-timeout e --no-recreate.') }
    }
    if ($env:COMPOSE_PROJECT_NAME -and $env:COMPOSE_PROJECT_NAME -ne 'backend') { $failures.Add('COMPOSE_PROJECT_NAME seleciona outro projeto. Revise o override antes de continuar.') }
    foreach ($failure in $failures) { Write-Host "PENDENTE: $failure" -ForegroundColor Yellow }
    if ($failures.Count -gt 0) { throw 'Pre-requisitos incompletos. Nenhuma alteracao foi realizada; corrija os itens e execute -CheckOnly novamente.' }
    Write-Host 'Git, Docker Linux/Compose, SDK .NET 10+, Node.js e npm: OK.' -ForegroundColor Green
}

function Invoke-SetupHelper {
    param([string]$Action)
    if (-not (Test-Path -LiteralPath $script:HelperDll)) { throw 'Utilitario local ausente. Execute .\setup.ps1 para compilar e preparar o ambiente.' }
    Invoke-Checked -Command dotnet -Arguments @($script:HelperDll, $Action, $script:RepoRoot) -Failure "Etapa '$Action' interrompida. Siga a mensagem acima; nenhuma repeticao automatica de escrita sera feita."
}

function Get-FrontendConfiguration {
    $result = & node (Join-Path $script:RepoRoot 'scripts/frontend-config.mjs')
    if ($LASTEXITCODE -ne 0) { throw 'Configuracao Vite invalida. Revise overrides locais e execute npm ci em src/frontend.' }
    return ($result | ConvertFrom-Json)
}

function Test-CertificatePair {
    param($Config)
    $previousCert = $env:SUPPORTATOS_HTTPS_CERT
    $previousKey = $env:SUPPORTATOS_HTTPS_KEY
    try {
        $env:SUPPORTATOS_HTTPS_CERT = $Config.cert
        $env:SUPPORTATOS_HTTPS_KEY = $Config.key
        Invoke-SetupHelper 'certificate'
    } finally {
        $env:SUPPORTATOS_HTTPS_CERT = $previousCert
        $env:SUPPORTATOS_HTTPS_KEY = $previousKey
    }
}

function Initialize-DevelopmentCertificate {
    $config = Get-FrontendConfiguration
    $certExists = Test-Path -LiteralPath $config.cert
    $keyExists = Test-Path -LiteralPath $config.key
    if ($certExists -ne $keyExists) { throw 'Par HTTPS incompleto. Restaure o arquivo ausente; o setup nao sobrescreve arquivos existentes.' }
    if (-not $certExists -and $config.custom) { throw 'Certificado HTTPS personalizado ausente. Corrija SUPPORTATOS_HTTPS_CERT/KEY sem sobrescrever arquivos.' }
    & dotnet dev-certs https --check --trust *> $null
    if ($LASTEXITCODE -ne 0) {
        Write-Host 'O Windows pode solicitar confirmacao para confiar no certificado local do ASP.NET Core.'
        Invoke-Checked -Command dotnet -Arguments @('dev-certs', 'https', '--trust') -Failure 'Certificado nao confiavel. Aceite a solicitacao do Windows e execute novamente; nao desative TLS.'
    }
    if (-not $certExists) {
        # Export into a new temporary directory, then use non-overwriting copies.
        $temporary = Join-Path ([System.IO.Path]::GetTempPath()) ('supportatos-cert-' + [guid]::NewGuid().ToString('N'))
        $null = New-Item -ItemType Directory -Path $temporary
        try {
            $export = Join-Path $temporary 'frontend.pem'
            Invoke-Checked -Command dotnet -Arguments @('dev-certs', 'https', '--export-path', $export, '--format', 'Pem', '--no-password') -Failure 'Exportacao HTTPS falhou. Confira o SDK e o certificado local.'
            $null = New-Item -ItemType Directory -Path (Split-Path -Parent $config.cert) -Force
            [System.IO.File]::Copy($export, $config.cert, $false)
            [System.IO.File]::Copy((Join-Path $temporary 'frontend.key'), $config.key, $false)
        } finally {
            # Delete only the two files created in this unique temporary directory.
            foreach ($name in @('frontend.pem', 'frontend.key')) {
                $file = Join-Path $temporary $name
                if (Test-Path -LiteralPath $file) { Remove-Item -LiteralPath $file }
            }
            Remove-Item -LiteralPath $temporary
        }
    }
    Test-CertificatePair $config
}
