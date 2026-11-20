#Requires -Version 5.1
[CmdletBinding()]
param([switch]$CheckOnly, [switch]$ConnectionOnly)

. (Join-Path $PSScriptRoot 'scripts/Development.Common.ps1')
Push-Location $script:RepoRoot
$previousConnectionEnvironment = Get-ProcessConnectionEnvironment
$previousDbPort = $env:SUPPORTATOS_DB_PORT
try {
    Test-DevelopmentPrerequisites
    if ($CheckOnly) {
        if (-not (Show-DevelopmentConnectionDiagnostics)) { exit 2 }
        Write-Host 'CheckOnly concluido: sem restore, instalacoes, banco, containers, segredos ou certificados alterados.'
        return
    }
    if ([Console]::IsInputRedirected -or [Console]::IsOutputRedirected) { throw 'Execute o setup em terminal interativo, sem pipe/transcript de segredos.' }
    Write-Host 'Restaurando ferramenta local fixada: dotnet-ef 10.0.0.'
    Invoke-Checked -Command dotnet -Arguments @('tool', 'restore', '--tool-manifest', (Join-Path $PSScriptRoot '.config/dotnet-tools.json')) -Failure 'Restore do EF falhou. Confira acesso ao NuGet; nao sera usada a ferramenta global.'
    Invoke-Checked -Command dotnet -Arguments @('tool', 'run', 'dotnet-ef', '--', '--version') -Failure 'EF local indisponivel. Revise .config/dotnet-tools.json e execute dotnet tool restore na raiz.'
    Invoke-Checked -Command dotnet -Arguments @('build', $script:HelperProject, '-c', 'Release', '--nologo', '-v', 'minimal') -Failure 'Build falhou. Confira NuGet/SDK; se houver DLL bloqueada, encerre somente a instancia correspondente da API.'
    Use-DockerDevelopmentConnection -Configure
    Save-SupportAtosDbPort
    if ($ConnectionOnly) {
        Write-Host 'Conexao Docker preparada e verificada. JWT, certificados, migrations e bootstrap nao foram executados.' -ForegroundColor Green
        return
    }
    Invoke-SetupHelper 'prepare'
    Invoke-SetupHelper 'docker'
    Invoke-SetupHelper 'status'
    Push-Location (Join-Path $PSScriptRoot 'src/frontend')
    try { Invoke-Checked -Command npm -Arguments @('ci') -Failure 'npm ci falhou. Confira Node/npm, rede e package-lock.json; nao use npm install para contornar o lockfile.' }
    finally { Pop-Location }
    Initialize-DevelopmentCertificate
    Invoke-SetupHelper 'migrate'
    Invoke-SetupHelper 'bootstrap'
    Write-Host 'Preparacao concluida. Execute .\start.ps1. Dados de SLA/grupos e vinculos funcionais nao sao criados por SQL legado.' -ForegroundColor Green
} catch {
    Write-Host ("ERRO: " + $_.Exception.Message) -ForegroundColor Red
    exit 1
} finally { if (-not $CheckOnly) { Restore-ProcessConnectionEnvironment $previousConnectionEnvironment }; $env:SUPPORTATOS_DB_PORT = $previousDbPort; Pop-Location }
