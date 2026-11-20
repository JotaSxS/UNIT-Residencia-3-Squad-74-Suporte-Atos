# Read-only diagnostics and scoped connection activation. No secrets are printed.
function Get-ApiSecretsPath {
    $project = Join-Path $script:RepoRoot 'src/backend/src/SupportAtos.Api/SupportAtos.Api.csproj'
    [xml]$xml = [System.IO.File]::ReadAllText($project)
    $id = $xml.SelectSingleNode('//*[local-name()="UserSecretsId"]').InnerText
    $appdata = $env:APPDATA
    if (-not $appdata) { $appdata = [Environment]::GetFolderPath('ApplicationData') }
    return (Join-Path $appdata "Microsoft/UserSecrets/$id/secrets.json")
}

function Get-JsonConnection {
    param([string]$Path)
    if (-not (Test-Path -LiteralPath $Path)) { return $null }
    try {
        $json = [System.IO.File]::ReadAllText($Path) | ConvertFrom-Json
        $flat = $json.PSObject.Properties | Where-Object Name -eq 'ConnectionStrings:DefaultConnection'
        if ($flat) { return [string]$flat.Value }
        $section = $json.PSObject.Properties | Where-Object Name -eq 'ConnectionStrings'
        if ($section) {
            $value = $section.Value.PSObject.Properties | Where-Object Name -eq 'DefaultConnection'
            if ($value) { return [string]$value.Value }
        }
    } catch { throw 'JSON de configuracao invalido para diagnostico. Corrija localmente sem publicar seu conteudo; nenhum arquivo foi alterado.' }
    return $null
}

function Test-ConnectionEnvironmentName {
    param([string]$Name)
    $Name = $Name -ireplace '^(ASPNETCORE_|DOTNET_)', ''
    return ($Name.Replace('__', ':') -ieq 'ConnectionStrings:DefaultConnection' -or
        $Name -imatch '^(CUSTOMCONNSTR_|SQLCONNSTR_|SQLAZURECONNSTR_|MYSQLCONNSTR_)DefaultConnection$')
}

function Get-ConnectionEnvironmentValues {
    param([string]$Scope)
    $result = @{}
    foreach ($entry in [Environment]::GetEnvironmentVariables($Scope).GetEnumerator()) {
        if (Test-ConnectionEnvironmentName ([string]$entry.Key)) { $result[[string]$entry.Key] = [string]$entry.Value }
    }
    return $result
}

function Get-ProcessConnectionEnvironment { return (Get-ConnectionEnvironmentValues 'Process') }

function Get-ConnectionClassification {
    param([string]$Value)
    if ([string]::IsNullOrWhiteSpace($Value)) { return 'ausente' }
    if ($Value.Contains('__SUA_SENHA__')) { return 'placeholder' }
    try {
        $builder = New-Object System.Data.Common.DbConnectionStringBuilder
        # Invoke CLR setter: PowerShell's IDictionary adapter treats property assignment as a key.
        $builder.set_ConnectionString($Value)
        # Npgsql aliases are accepted by runtime; this diagnostic is conservative.
        if ($builder.ContainsKey('Host') -and $builder.ContainsKey('Port') -and $builder.ContainsKey('Database') -and
            $builder.ContainsKey('Username') -and $builder.ContainsKey('Password') -and
            @('127.0.0.1', 'localhost') -contains [string]$builder['Host'] -and [int]$builder['Port'] -eq 5433 -and
            [string]$builder['Database'] -eq 'supportatos' -and [string]$builder['Username'] -eq 'admin' -and
            -not [string]::IsNullOrWhiteSpace([string]$builder['Password']) -and
            (-not $builder.ContainsKey('Options') -or [string]::IsNullOrWhiteSpace([string]$builder['Options'])) -and
            (-not $builder.ContainsKey('Search Path') -or [string]::IsNullOrWhiteSpace([string]$builder['Search Path']))) { return 'Docker oficial' }
    } catch { }
    return 'legada/divergente'
}

function Show-DevelopmentConnectionDiagnostics {
    Add-Type -AssemblyName System.Data
    $api = Join-Path $script:RepoRoot 'src/backend/src/SupportAtos.Api'
    $sources = @(
        @{ Label = 'appsettings.json'; Path = (Join-Path $api 'appsettings.json') },
        @{ Label = 'appsettings.Development.json'; Path = (Join-Path $api 'appsettings.Development.json') },
        @{ Label = 'User Secrets'; Path = (Get-ApiSecretsPath) }
    )
    $configured = $false
    $divergent = $false
    foreach ($source in $sources) {
        $classification = Get-ConnectionClassification (Get-JsonConnection $source.Path)
        Write-Host ("Conexao / " + $source.Label + ': ' + $classification)
        if ($classification -eq 'Docker oficial') { $configured = $true }
        if ($classification -eq 'legada/divergente') { $divergent = $true }
    }
    foreach ($scope in @('Process', 'User', 'Machine')) {
        foreach ($entry in (Get-ConnectionEnvironmentValues $scope).GetEnumerator()) {
            if (Test-ConnectionEnvironmentName ([string]$entry.Key)) {
                $classification = Get-ConnectionClassification ([string]$entry.Value)
                Write-Host ("Conexao / ambiente $scope / " + $entry.Key + ': ' + $classification)
                if ($classification -eq 'Docker oficial') { $configured = $true }
                else { $divergent = $true }
            }
        }
    }
    # launchSettings is not used by start.ps1, but report hidden overrides in manual profiles.
    try { $launch = Get-Content -LiteralPath (Join-Path $api 'Properties/launchSettings.json') -Raw | ConvertFrom-Json }
    catch { throw 'launchSettings.json invalido; confira o arquivo localmente sem publicar seu conteudo.' }
    foreach ($profile in $launch.profiles.PSObject.Properties) {
        $environment = $profile.Value.PSObject.Properties | Where-Object Name -eq 'environmentVariables'
        if ($environment) {
            foreach ($entry in $environment.Value.PSObject.Properties) {
                if (Test-ConnectionEnvironmentName $entry.Name) {
                    $classification = Get-ConnectionClassification ([string]$entry.Value)
                    Write-Host ("Conexao / launch profile / " + $profile.Name + ': ' + $classification)
                    if ($classification -ne 'Docker oficial') { $divergent = $true }
                }
            }
        }
    }
    $names = @(& docker ps -a --format '{{.Names}}')
    if ($LASTEXITCODE -ne 0) { throw 'Nao foi possivel inspecionar Docker. Nenhum container foi modificado.' }
    if ($names -contains 'supportatos-db') {
        $metadata = [string](& docker inspect supportatos-db --format '{{.Config.Image}}|{{range .Mounts}}{{.Name}};{{end}}|{{.State.Status}}')
        if ($LASTEXITCODE -ne 0) { throw 'Falha ao inspecionar container; nenhum recurso foi modificado.' }
        $labels = (& docker inspect supportatos-db --format '{{json .Config.Labels}}') | ConvertFrom-Json
        if ($LASTEXITCODE -ne 0 -or $metadata -notmatch '^postgres:17\|backend_supportatos-db-data;\|' -or
            $labels.'com.docker.compose.project' -ne 'backend') {
            Write-Host 'Docker existente diverge da identidade oficial; nenhuma recriacao sera realizada.' -ForegroundColor Yellow
            $divergent = $true
        } else { Write-Host 'Docker existente: PostgreSQL 17 / backend_supportatos-db-data; preservado, sem recriacao.' }
    } else { Write-Host 'Docker: container ausente. O setup interativo podera preparar o Compose oficial sem apagar volumes existentes.' }
    if ($divergent -or -not $configured) {
        Write-Host 'PENDENTE: execute setup.ps1 para confirmar a conexao Docker, backup protegido e overrides temporarios. PostgreSQL Windows nao sera acessado.' -ForegroundColor Yellow
        return $false
    }
    return $true
}

function Use-DockerDevelopmentConnection {
    param([switch]$Configure)
    $privateRoot = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'SupportAtos/setup-private'
    $ticket = Join-Path $privateRoot ('sessions/' + [guid]::NewGuid().ToString('N') + '.dpapi')
    $previousTicket = $env:SUPPORTATOS_SETUP_SESSION_FILE
    try {
        $env:SUPPORTATOS_SETUP_SESSION_FILE = $ticket
        if ($Configure) { Invoke-SetupHelper 'connection' } else { Invoke-SetupHelper 'session' }
        Add-Type -AssemblyName System.Security
        $bytes = [System.Security.Cryptography.ProtectedData]::Unprotect(
            [System.IO.File]::ReadAllBytes($ticket), $null, [System.Security.Cryptography.DataProtectionScope]::CurrentUser)
        try { $connection = [System.Text.Encoding]::UTF8.GetString($bytes) }
        finally { [Array]::Clear($bytes, 0, $bytes.Length) }
        if ((Get-ConnectionClassification $connection) -ne 'Docker oficial') { throw 'Canal de sessao nao contem o destino Docker oficial.' }
        foreach ($name in (Get-ProcessConnectionEnvironment).Keys) { [Environment]::SetEnvironmentVariable($name, $null, 'Process') }
        [Environment]::SetEnvironmentVariable('ConnectionStrings__DefaultConnection', $connection, 'Process')
    } finally {
        $env:SUPPORTATOS_SETUP_SESSION_FILE = $previousTicket
        if (Test-Path -LiteralPath $ticket) { Remove-Item -LiteralPath $ticket }
    }
}

function Restore-ProcessConnectionEnvironment {
    param([hashtable]$Previous)
    foreach ($name in (Get-ProcessConnectionEnvironment).Keys) { [Environment]::SetEnvironmentVariable($name, $null, 'Process') }
    foreach ($entry in $Previous.GetEnumerator()) { [Environment]::SetEnvironmentVariable($entry.Key, $entry.Value, 'Process') }
}
