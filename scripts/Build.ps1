param([ValidateSet('Debug','Release')][string]$Configuration = 'Release')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
Push-Location $projectRoot
try {
    $dll = Join-Path $projectRoot 'third_party\VirtualDesktopAccessor\VirtualDesktopAccessor.dll'
    $expected = '8740C572A1C000E3B87FFEB1E4C397EAE9AF3BD4A2ABDC3BCFFACAB4493F8FF5'
    if (!(Test-Path -LiteralPath $dll) -or (Get-FileHash -LiteralPath $dll).Hash -ne $expected) {
        throw 'VirtualDesktopAccessor.dll is missing or its SHA-256 differs from the pinned release.'
    }
    & dotnet restore WorkspaceManager.slnx -p:Platform=x64 --locked-mode
    if ($LASTEXITCODE -ne 0) { throw 'Dependency restore failed.' }
    & dotnet build WorkspaceManager.slnx -c $Configuration -p:Platform=x64 --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
} finally { Pop-Location }
