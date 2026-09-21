param([ValidateSet('Debug','Release')][string]$Configuration = 'Release')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$published = Join-Path $projectRoot 'artifacts\publish\win-x64\WorkspaceManager.App.exe'
if (Test-Path $published) {
    Start-Process -FilePath $published
} else {
    & "$PSScriptRoot\Build.ps1" -Configuration $Configuration
    $exe = Join-Path $projectRoot "src\WorkspaceManager.App\bin\x64\$Configuration\net10.0-windows10.0.26100.0\win-x64\WorkspaceManager.App.exe"
    Start-Process -FilePath $exe
}
