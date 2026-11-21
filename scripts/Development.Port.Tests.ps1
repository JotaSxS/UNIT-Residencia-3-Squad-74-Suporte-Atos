. (Join-Path $PSScriptRoot 'Development.Common.ps1')
$container = '{"Config":{"Image":"postgres:17","Labels":{"com.docker.compose.project":"backend","com.docker.compose.service":"supportatos-db"}},"Mounts":[{"Type":"volume","Destination":"/var/lib/postgresql/data","Name":"backend_supportatos-db-data"}],"HostConfig":{"PortBindings":{"5432/tcp":[{"HostIp":"127.0.0.1","HostPort":"5434"}]}},"State":{"Running":false}}' | ConvertFrom-Json
if ((Get-OfficialContainerPort $container) -ne 5434) { throw 'Porta de container parado nao recuperada.' }
$container.HostConfig.PortBindings.'5432/tcp'[0].HostIp = '0.0.0.0'
$refused = $false
try { Get-OfficialContainerPort $container } catch { $refused = $true }
if (-not $refused) { throw 'Binding inseguro aceito.' }
$env:SUPPORTATOS_DB_PORT = $null
$script:RepoRoot = Join-Path ([IO.Path]::GetTempPath()) ([guid]::NewGuid().ToString())
function docker {
    $global:LASTEXITCODE = 0
    if ($args[0] -eq 'ps') { return 'supportatos-db' }
    return ($script:fixture | ConvertTo-Json -Depth 10 -Compress)
}
$container.HostConfig.PortBindings.'5432/tcp'[0].HostIp = '127.0.0.1'
$script:fixture = $container
Initialize-SupportAtosDbPort
if ((Get-SupportAtosDbPort) -ne 5434 -or $env:SUPPORTATOS_DB_PORT) { throw 'Descoberta deve preservar ambiente.' }
if (Test-Path $script:RepoRoot) { throw 'Descoberta criou arquivos.' }
$env:SUPPORTATOS_DB_PORT = '5440'
Initialize-SupportAtosDbPort
if ((Get-SupportAtosDbPort) -ne 5440) { throw 'Porta explicita ignorada.' }
$env:SUPPORTATOS_DB_PORT = $null
function docker { $global:LASTEXITCODE = 0 }
function Test-DbPortAvailable { param($Port) return ($Port -eq 5435) }
Initialize-SupportAtosDbPort
if ((Get-SupportAtosDbPort) -ne 5435) { throw 'Alternativa livre incorreta.' }
foreach ($value in @('outro-servico', ' supportatos-db')) {
    $container.Config.Labels.'com.docker.compose.service' = $value
    $refused = $false
    try { Get-OfficialContainerPort $container } catch { $refused = $true }
    if (-not $refused) { throw 'Servico divergente aceito.' }
}
$container.Config.Labels.'com.docker.compose.service' = 'supportatos-db'
$container.Mounts[0].Type = 'bind'
$refused = $false
try { Get-OfficialContainerPort $container } catch { $refused = $true }
if (-not $refused) { throw 'Mount sem volume Docker aceito.' }
$fixtureDirectory = [IO.Path]::Combine([IO.Path]::GetTempPath(), [guid]::NewGuid().ToString())
[IO.Directory]::CreateDirectory($fixtureDirectory) | Out-Null
$script:RepoRoot = $fixtureDirectory
$file = Join-Path $fixtureDirectory '.supportatos.local.env'
try {
    foreach ($content in @('# sem porta', 'OUTRA_CONFIG=valor', 'SUPPORTATOS_DB_PORT=', 'SUPPORTATOS_DB_PORT=abc', 'SUPPORTATOS_DB_PORT=65536')) {
        [IO.File]::WriteAllText($file, $content)
        $refused = $false
        try { Initialize-SupportAtosDbPort } catch { $refused = $_.Exception.Message -match '\.supportatos.local.env.*(nao contem|invalida)' }
        if (-not $refused) { throw 'Arquivo sem porta valida aceito.' }
        if ([IO.File]::ReadAllText($file) -cne $content) { throw 'Arquivo existente alterado.' }
    }
} finally {
    [IO.File]::Delete($file)
    [IO.Directory]::Delete($fixtureDirectory)
}
Write-Host '13 cenarios sinteticos aprovados.'
