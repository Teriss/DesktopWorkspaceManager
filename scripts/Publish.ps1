param([switch]$Zip)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
& "$PSScriptRoot\Build.ps1" -Configuration Release
Push-Location $projectRoot
try {
    $publishDir = Join-Path $projectRoot 'artifacts\publish\win-x64'
    & dotnet publish src\WorkspaceManager.App -c Release -r win-x64 -p:Platform=x64 --self-contained true --no-restore -o $publishDir
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
    # Validate the actual distributable, including PRI/XBF, before producing a ZIP.
    $started = Get-Date
    $publishedExe = Join-Path $publishDir 'WorkspaceManager.App.exe'
    $testProcess = Start-Process -FilePath $publishedExe -ArgumentList '--smoke-test' -WindowStyle Hidden -PassThru
    if (!$testProcess.WaitForExit(30000)) { throw "Published UI initialization timed out, PID $($testProcess.Id)." }
    $reportPath = Join-Path $env:LOCALAPPDATA 'DesktopWorkspaceManager\ui-smoke-test.json'
    if ($testProcess.ExitCode -ne 0 -or !(Test-Path -LiteralPath $reportPath) -or (Get-Item -LiteralPath $reportPath).LastWriteTime -lt $started) { throw 'Published application failed its initialization check.' }
    $report = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
    if (!$report.Success) { throw ($report | ConvertTo-Json -Depth 4) }
    $validationDir = Join-Path $projectRoot 'artifacts\validation'
    New-Item -ItemType Directory -Force -Path $validationDir | Out-Null
    Copy-Item -LiteralPath $reportPath -Destination (Join-Path $validationDir 'published-ui-smoke-test.json')
    Copy-Item -LiteralPath (Join-Path $projectRoot 'README.md') -Destination $publishDir
    Copy-Item -LiteralPath (Join-Path $projectRoot 'LICENSE') -Destination $publishDir
    Copy-Item -LiteralPath (Join-Path $projectRoot 'docs\VALIDATION.md') -Destination $publishDir
    $docsDir = Join-Path $publishDir 'docs'
    New-Item -ItemType Directory -Force -Path $docsDir | Out-Null
    Copy-Item -LiteralPath (Join-Path $projectRoot 'docs\VALIDATION.md') -Destination $docsDir
    $licenseDir = Join-Path $publishDir 'licenses\VirtualDesktopAccessor'
    New-Item -ItemType Directory -Force -Path $licenseDir | Out-Null
    Copy-Item -LiteralPath (Join-Path $projectRoot 'third_party\VirtualDesktopAccessor\LICENSE.txt') -Destination $licenseDir
    Copy-Item -LiteralPath (Join-Path $projectRoot 'third_party\VirtualDesktopAccessor\README.md') -Destination $licenseDir
    Copy-Item -LiteralPath (Join-Path $projectRoot 'THIRD-PARTY-NOTICES.md') -Destination $publishDir
    $assets = Get-Content (Join-Path $projectRoot 'src\WorkspaceManager.App\obj\project.assets.json') -Raw | ConvertFrom-Json
    $nugetRoot = $assets.packageFolders.PSObject.Properties.Name | Select-Object -First 1
    $packagePaths = @($assets.libraries.PSObject.Properties | Where-Object { $_.Value.type -eq 'package' } | ForEach-Object { $_.Name })
    $packagePaths += 'Microsoft.NETCore.App.Runtime.win-x64/10.0.8'
    foreach ($package in ($packagePaths | Select-Object -Unique)) {
        $packageDir = Join-Path $nugetRoot $package.ToLowerInvariant()
        if (Test-Path -LiteralPath $packageDir) {
            $notices = @(Get-ChildItem -LiteralPath $packageDir -File | Where-Object { $_.Name -match '^(license|notice|third.party)' })
            if ($notices.Count -gt 0) {
                $destination = Join-Path $publishDir ('licenses\' + ($package -replace '/', '-'))
                New-Item -ItemType Directory -Force -Path $destination | Out-Null
                foreach ($notice in $notices) { Copy-Item -LiteralPath $notice.FullName -Destination $destination }
            }
        }
    }
    $hashes = Get-ChildItem -LiteralPath $publishDir -Recurse -File | Where-Object { $_.Name -ne 'SHA256SUMS.txt' } | ForEach-Object {
        '{0}  {1}' -f (Get-FileHash -LiteralPath $_.FullName).Hash, $_.FullName.Substring($publishDir.Length + 1)
    }
    $hashes | Set-Content -LiteralPath (Join-Path $publishDir 'SHA256SUMS.txt') -Encoding utf8
    if ($Zip) {
        $project = [xml](Get-Content -LiteralPath (Join-Path $projectRoot 'src\WorkspaceManager.App\WorkspaceManager.App.csproj') -Raw)
        $version = $project.Project.PropertyGroup.Version
        Compress-Archive -Path (Join-Path $publishDir '*') -DestinationPath (Join-Path $projectRoot "artifacts\DesktopWorkspaceManager-$version-win-x64.zip") -Force
    }
    Write-Output "Published: $publishDir"
} finally { Pop-Location }
