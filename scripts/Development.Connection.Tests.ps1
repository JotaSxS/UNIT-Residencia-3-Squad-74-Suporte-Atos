#Requires -Version 5.1
# Synthetic files and current-process environment only. Never calls Docker/database.
. (Join-Path $PSScriptRoot 'Development.Common.ps1')
Add-Type -AssemblyName System.Data
$env:SUPPORTATOS_DB_PORT = '5433'
$docker = 'Host=127.0.0.1;Port=5433;Database=supportatos;Username=admin;Password=synthetic-only'
$dockerAlt = 'Host=127.0.0.1;Port=5434;Database=supportatos;Username=admin;Password=synthetic-only'
$old = 'Host=localhost;Port=5432;Database=supportatos;Username=postgres;Password=old-only'
$passed = 0
foreach ($case in @(
    @{ Value = $docker; Expected = 'Docker oficial' },
    @{ Value = $old; Expected = 'legada/divergente' },
    @{ Value = ''; Expected = 'ausente' },
    @{ Value = 'Host=127.0.0.1;Password=__SUA_SENHA__'; Expected = 'placeholder' }
)) {
    if ((Get-ConnectionClassification $case.Value) -ne $case.Expected) { throw 'Classificacao de conexao incorreta.' }
    $passed++
}
$env:SUPPORTATOS_DB_PORT = '5434'
foreach ($case in @(
    @{ Value = $dockerAlt; Expected = 'Docker oficial' },
    @{ Value = $docker; Expected = 'legada/divergente' }
)) {
    if ((Get-ConnectionClassification $case.Value) -ne $case.Expected) { throw 'Classificacao da porta alternativa incorreta.' }
    $passed++
}
$env:SUPPORTATOS_DB_PORT = '5433'
foreach ($name in @('ConnectionStrings__DefaultConnection', 'ConnectionStrings:DefaultConnection', 'CUSTOMCONNSTR_DefaultConnection', 'ASPNETCORE_ConnectionStrings__DefaultConnection', 'DOTNET_ConnectionStrings__DefaultConnection')) {
    if (-not (Test-ConnectionEnvironmentName $name)) { throw 'Alias de ambiente nao detectado.' }
    $passed++
}
$directory = Join-Path ([System.IO.Path]::GetTempPath()) ('supportatos-json-test-' + [guid]::NewGuid().ToString('N'))
$null = New-Item -ItemType Directory -Path $directory
$file = Join-Path $directory 'synthetic.json'
try {
    if ($null -ne (Get-JsonConnection $file)) { throw 'Clone limpo deveria indicar ausencia.' }
    $passed++
    [IO.File]::WriteAllText($file, (@{ ConnectionStrings = @{ DefaultConnection = $old }; Other = 'untouched' } | ConvertTo-Json))
    $before = [IO.File]::ReadAllBytes($file)
    if ((Get-JsonConnection $file) -ne $old) { throw 'User Secrets antigo nao detectado.' }
    if ([Convert]::ToBase64String($before) -ne [Convert]::ToBase64String([IO.File]::ReadAllBytes($file))) { throw 'Diagnostico alterou o fixture.' }
    $passed++
} finally { if (Test-Path -LiteralPath $file) { Remove-Item -LiteralPath $file }; Remove-Item -LiteralPath $directory }
# Exercise the full read-only diagnostic with synthetic config and Docker metadata.
$fixtureRoot = Join-Path ([IO.Path]::GetTempPath()) ('supportatos-diagnostic-test-' + [guid]::NewGuid().ToString('N'))
$api = Join-Path $fixtureRoot 'src/backend/src/SupportAtos.Api'
$properties = Join-Path $api 'Properties'
$null = New-Item -ItemType Directory -Path $properties
$stored = Join-Path $fixtureRoot 'synthetic-secrets.json'
$files = @((Join-Path $api 'appsettings.json'), (Join-Path $api 'appsettings.Development.json'), (Join-Path $properties 'launchSettings.json'))
[IO.File]::WriteAllText($files[0], '{}')
[IO.File]::WriteAllText($files[1], '{"ConnectionStrings":{"DefaultConnection":"Host=127.0.0.1;Port=5433;Database=supportatos;Username=admin;Password=__SUA_SENHA__"}}')
[IO.File]::WriteAllText($files[2], '{"profiles":{}}')
$originalRoot = $script:RepoRoot
$script:RepoRoot = $fixtureRoot
function Get-ApiSecretsPath { return $stored }
function Get-ConnectionEnvironmentValues {
    param([string]$Scope)
    if ($script:ConnectionScenario -eq 'Legacy' -and $Scope -eq 'Process') { return @{ CUSTOMCONNSTR_DefaultConnection = $old } }
    return @{}
}
function docker {
    $global:LASTEXITCODE = 0
    if ($args[0] -notin @('ps', 'inspect')) { throw 'Diagnostico tentou alterar Docker ou consultar banco.' }
    if ($args[0] -eq 'ps') { if ($script:ConnectionScenario -ne 'Clean') { 'supportatos-db' }; return }
    if ($args[3] -eq '{{json .Config.Labels}}') { '{"com.docker.compose.project":"backend"}'; return }
    if ($args[3] -eq '{{json .HostConfig.PortBindings}}') {
        '{"5432/tcp":[{"HostIp":"' + [string]$script:ContainerHostIp + '","HostPort":"' + [string]$script:ContainerPort + '"}]}'
        return
    }
    if ($script:ConnectionScenario -eq 'WrongVolume') { 'postgres:17|another-volume;|running' }
    else { 'postgres:17|backend_supportatos-db-data;|running' }
}
try {
    foreach ($case in @(
        @{ Scenario = 'Legacy'; Expected = $false; Port = 5433; ContainerPort = 5433; ContainerHostIp = '127.0.0.1'; Connection = $old },
        @{ Scenario = 'Clean'; Expected = $false; Port = 5433; ContainerPort = 5433; ContainerHostIp = '127.0.0.1'; Connection = $null },
        @{ Scenario = 'Existing'; Expected = $true; Port = 5433; ContainerPort = 5433; ContainerHostIp = '127.0.0.1'; Connection = $docker },
        @{ Scenario = 'ExistingAlt'; Expected = $true; Port = 5434; ContainerPort = 5434; ContainerHostIp = '127.0.0.1'; Connection = $dockerAlt },
        @{ Scenario = 'WrongVolume'; Expected = $false; Port = 5433; ContainerPort = 5433; ContainerHostIp = '127.0.0.1'; Connection = $docker },
        @{ Scenario = 'WrongPort'; Expected = $false; Port = 5434; ContainerPort = 5433; ContainerHostIp = '127.0.0.1'; Connection = $dockerAlt },
        @{ Scenario = 'WildcardHost'; Expected = $false; Port = 5433; ContainerPort = 5433; ContainerHostIp = '0.0.0.0'; Connection = $docker },
        @{ Scenario = 'Ipv6AnyHost'; Expected = $false; Port = 5433; ContainerPort = 5433; ContainerHostIp = '::'; Connection = $docker },
        @{ Scenario = 'OtherHost'; Expected = $false; Port = 5433; ContainerPort = 5433; ContainerHostIp = '10.0.0.5'; Connection = $docker },
        @{ Scenario = 'EmptyHost'; Expected = $false; Port = 5433; ContainerPort = 5433; ContainerHostIp = ''; Connection = $docker }
    )) {
        $script:ConnectionScenario = $case.Scenario
        $script:ContainerPort = $case.ContainerPort
        $script:ContainerHostIp = $case.ContainerHostIp
        $env:SUPPORTATOS_DB_PORT = [string]$case.Port
        if (Test-Path -LiteralPath $stored) { Remove-Item -LiteralPath $stored }
        if ($null -ne $case.Connection) { [IO.File]::WriteAllText($stored, (@{ 'ConnectionStrings:DefaultConnection' = $case.Connection } | ConvertTo-Json)) }
        $before = if (Test-Path -LiteralPath $stored) { [IO.File]::ReadAllText($stored) } else { $null }
        $result = Show-DevelopmentConnectionDiagnostics 6>$null
        if ($result -ne $case.Expected) { throw "Diagnostico incorreto: $($case.Scenario)." }
        $after = if (Test-Path -LiteralPath $stored) { [IO.File]::ReadAllText($stored) } else { $null }
        if ($before -cne $after) { throw 'Diagnostico alterou a configuracao sintetica.' }
        $passed++
    }
} finally {
    $script:RepoRoot = $originalRoot
    Remove-Item Function:\docker
    foreach ($file in ($files + @($stored))) { if (Test-Path -LiteralPath $file) { Remove-Item -LiteralPath $file } }
    foreach ($path in @($properties, $api, (Join-Path $fixtureRoot 'src/backend/src'), (Join-Path $fixtureRoot 'src/backend'), (Join-Path $fixtureRoot 'src'), $fixtureRoot)) { Remove-Item -LiteralPath $path }
}
Write-Host "$passed testes de diagnostico de conexao aprovados."
