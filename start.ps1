#Requires -Version 5.1
[CmdletBinding()]
param(
    [ValidateSet('All', 'Api', 'Frontend')][string]$Service = 'All',
    [switch]$CheckOnly
)

. (Join-Path $PSScriptRoot 'scripts/Development.Common.ps1')

function Start-SupportAtosTerminal {
    param([string]$Title, [string]$WorkingDirectory, [string]$Command)
    # Same PowerShell console host as the caller (5.1 or 7), even if invoked from ISE.
    $shell = if ($PSVersionTable.PSEdition -eq 'Core') {
        Join-Path $PSHOME $(if ($IsWindows) { 'pwsh.exe' } else { 'pwsh' })
    } else {
        Join-Path $PSHOME 'powershell.exe'
    }
    $bootstrap = "`$Host.UI.RawUI.WindowTitle = '{0}'`r`n{1}" -f $Title.Replace("'", "''"), $Command
    $encoded = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($bootstrap))
    $terminal = Start-Process -FilePath $shell `
        -ArgumentList @('-NoLogo', '-NoProfile', '-NoExit', '-EncodedCommand', $encoded) `
        -WorkingDirectory $WorkingDirectory -WindowStyle Normal -PassThru
    if ($null -eq $terminal) { throw "Nao foi possivel abrir o terminal $Title." }
    return $terminal
}

Push-Location $script:RepoRoot
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
    } elseif ($Service -eq 'Frontend') {
        Write-Host 'Frontend: https://localhost:5173 | API: https://localhost:7099. Ctrl+C encerra esta sessao.'
        Push-Location (Join-Path $PSScriptRoot 'src/frontend')
        try { Invoke-Checked -Command npm -Arguments @('run', 'dev') -Failure 'Vite encerrou com erro. Confira a mensagem acima.' }
        finally { Pop-Location }
    } else {
        $backendCommand = "& dotnet '{0}'" -f $apiDll.Replace("'", "''")
        $null = Start-SupportAtosTerminal -Title 'SupportAtos Backend' -WorkingDirectory $apiDirectory -Command $backendCommand
        $ready = $false
        for ($attempt = 0; $attempt -lt 30; $attempt++) {
            try {
                $response = Invoke-WebRequest -Uri 'https://localhost:7099/health' -UseBasicParsing -TimeoutSec 2
                if ($response.StatusCode -eq 200) { $ready = $true; break }
            } catch { Start-Sleep -Seconds 1 }
        }
        if (-not $ready) { throw 'API nao ficou pronta com HTTPS confiavel. Veja os logs no terminal SupportAtos Backend e revise o certificado.' }
        $null = Start-SupportAtosTerminal -Title 'SupportAtos Frontend' `
            -WorkingDirectory (Join-Path $PSScriptRoot 'src/frontend') -Command '& npm run dev'
        Write-Host 'Terminais abertos: SupportAtos Backend (API https://localhost:7099) e SupportAtos Frontend (Vite https://localhost:5173).'
    }
} catch {
    Write-Host ("ERRO: " + $_.Exception.Message) -ForegroundColor Red
    exit 1
} finally {
    $env:DOTNET_ENVIRONMENT = $previousDotnet
    $env:ASPNETCORE_ENVIRONMENT = $previousAspnet
    $env:ASPNETCORE_URLS = $previousUrls
    if (-not $CheckOnly) { Restore-ProcessConnectionEnvironment $previousConnectionEnvironment }
    Pop-Location
}
