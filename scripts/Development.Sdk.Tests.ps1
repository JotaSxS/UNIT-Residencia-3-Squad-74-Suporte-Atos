#Requires -Version 5.1
# No Pester dependency, external processes or machine configuration changes.
. (Join-Path $PSScriptRoot 'Development.Common.ps1')
$passed = 0
foreach ($case in @(
    @{ Version = '10.0.100'; Preview = $false },
    @{ Version = '10.0.100-preview.7'; Preview = $true },
    @{ Version = '11.0.100-rc.3.26522.148'; Preview = $true },
    @{ Version = '11.0.100'; Preview = $false },
    @{ Version = '12.0.100-preview.1'; Preview = $true }
)) {
    $result = Get-DotnetSdkSupport $case.Version
    if ($result.Version -ne $case.Version -or $result.IsPreview -ne $case.Preview) { throw 'Classificacao de SDK incorreta.' }
    $passed++
}
foreach ($version in @('9.0.300', '8.0.100-preview.1', '', 'invalid')) {
    $rejected = $false
    try { $null = Get-DotnetSdkSupport $version } catch { $rejected = $true }
    if (-not $rejected) { throw 'SDK antigo/invalido foi aceito.' }
    $passed++
}
# Isolated command double: exercise compatibility failures, not the real CLI.
function dotnet {
    $global:LASTEXITCODE = 0
    switch ($args[0]) {
        '--version' { '11.0.100-preview.1' }
        '--list-runtimes' {
            'Microsoft.AspNetCore.App 10.0.14 [synthetic]'
            if ($script:SdkScenario -ne 'MissingRuntime') { 'Microsoft.NETCore.App 10.0.14 [synthetic]' }
        }
        'msbuild' {
            if ($script:SdkScenario -eq 'ProjectFailure') { $global:LASTEXITCODE = 1 }
            else { '{"Properties":{"TargetFramework":"net10.0","NETCoreSdkVersion":"11.0.100-preview.1"}}' }
        }
        'tool' {
            if ($script:SdkScenario -eq 'ToolMissing') { $global:LASTEXITCODE = 1 }
            elseif ($script:SdkScenario -eq 'ToolMismatch') { '11.0.0' }
            else { '10.0.0' }
        }
        default { throw 'Comando inesperado no teste SDK.' }
    }
}
try {
    foreach ($case in @(
        @{ Scenario = 'Compatible'; Reject = $false },
        @{ Scenario = 'MissingRuntime'; Reject = $true },
        @{ Scenario = 'ProjectFailure'; Reject = $true },
        @{ Scenario = 'ToolMismatch'; Reject = $true },
        @{ Scenario = 'ToolMissing'; Reject = $false }
    )) {
        $script:SdkScenario = $case.Scenario
        $rejected = $false
        try { Test-DotnetCompatibility 6>$null } catch { $rejected = $true }
        if ($rejected -ne $case.Reject) { throw "Validacao de compatibilidade incorreta: $($case.Scenario)." }
        $passed++
    }
} finally { Remove-Item Function:\dotnet }
Write-Host "$passed testes de SDK/compatibilidade aprovados."
