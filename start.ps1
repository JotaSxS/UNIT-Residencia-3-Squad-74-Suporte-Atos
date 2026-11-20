#Requires -Version 5.1
[CmdletBinding()]
param(
    [ValidateSet('All', 'Api', 'Frontend')][string]$Service = 'All',
    [switch]$CheckOnly
)

. (Join-Path $PSScriptRoot 'scripts/Development.Common.ps1')
Push-Location $script:RepoRoot
$apiProcess = $null
$previousDotnet = $env:DOTNET_ENVIRONMENT
$previousAspnet = $env:ASPNETCORE_ENVIRONMENT
$previousUrls = $env:ASPNETCORE_URLS
$previousConnectionEnvironment = Get-ProcessConnectionEnvironment
try {
    Test-DevelopmentPrerequisites
    if ($CheckOnly) {
        if (-not (Show-DevelopmentConnectionDiagnostics)) { exit 2 }
        Write-Host 'CheckOnly concluido; nenhum servico iniciado.'
        return
    }
    Use-DockerDevelopmentConnection
    Invoke-SetupHelper 'inspect'
    Invoke-SetupHelper 'status-current'
    $config = Get-FrontendConfiguration
    Test-CertificatePair $config
    if (-not (Test-Path (Join-Path $PSScriptRoot 'src/frontend/node_modules'))) { throw 'Dependencias frontend ausentes. Execute .\setup.ps1.' }
    $ports = @()
    if ($Service -in @('All', 'Api')) { $ports += 7099 }
    if ($Service -in @('All', 'Frontend')) { $ports += 5173 }
    foreach ($port in $ports) {
        if (Get-NetTCPConnection -State Listen -LocalPort $port -ErrorAction SilentlyContinue) { throw "Porta $port em uso. Identifique a instancia existente; nenhum processo sera encerrado." }
    }
    $env:DOTNET_ENVIRONMENT = 'Development'
    $env:ASPNETCORE_ENVIRONMENT = 'Development'
    $env:ASPNETCORE_URLS = 'https://localhost:7099'
    $apiDirectory = Join-Path $PSScriptRoot 'src/backend/src/SupportAtos.Api'
    $apiDll = Join-Path $apiDirectory 'bin/Release/net10.0/SupportAtos.Api.dll'
    if ($Service -in @('All', 'Api') -and -not (Test-Path -LiteralPath $apiDll)) { throw 'Build da API ausente. Execute .\setup.ps1.' }
    if ($Service -eq 'Api') {
        Push-Location $apiDirectory
        try { Invoke-Checked -Command dotnet -Arguments @($apiDll) -Failure 'API encerrou com erro. Confira a mensagem acima.' }
        finally { Pop-Location }
    } else {
        if ($Service -eq 'All') {
            $apiProcess = Start-Process -FilePath (Get-Command dotnet).Source -ArgumentList @(('"' + $apiDll + '"')) -WorkingDirectory $apiDirectory -WindowStyle Hidden -PassThru
            $ready = $false
            for ($attempt = 0; $attempt -lt 30; $attempt++) {
                if ($apiProcess.HasExited) { throw 'API encerrou durante a inicializacao. Execute .\start.ps1 -Service Api para ver o erro.' }
                try {
                    $response = Invoke-WebRequest -Uri 'https://localhost:7099/health' -UseBasicParsing -TimeoutSec 2
                    if ($response.StatusCode -eq 200) { $ready = $true; break }
                } catch { Start-Sleep -Seconds 1 }
            }
            if (-not $ready) { throw 'API nao ficou pronta com HTTPS confiavel. Execute -Service Api e revise o certificado.' }
        }
        Write-Host 'Frontend: https://localhost:5173 | API: https://localhost:7099. Ctrl+C encerra esta sessao.'
        Push-Location (Join-Path $PSScriptRoot 'src/frontend')
        try { Invoke-Checked -Command npm -Arguments @('run', 'dev') -Failure 'Vite encerrou com erro. Confira a mensagem acima.' }
        finally { Pop-Location }
    }
} catch {
    Write-Host ("ERRO: " + $_.Exception.Message) -ForegroundColor Red
    exit 1
} finally {
    # Only the API process created by this invocation is owned by this script.
    if ($null -ne $apiProcess -and -not $apiProcess.HasExited) { $apiProcess.Kill(); $apiProcess.WaitForExit() }
    $env:DOTNET_ENVIRONMENT = $previousDotnet
    $env:ASPNETCORE_ENVIRONMENT = $previousAspnet
    $env:ASPNETCORE_URLS = $previousUrls
    if (-not $CheckOnly) { Restore-ProcessConnectionEnvironment $previousConnectionEnvironment }
    Pop-Location
}
