param([switch]$Integration, [switch]$Preview, [switch]$Close, [switch]$UiSmoke, [switch]$UiInteraction, [switch]$UiClose, [ValidateSet('Debug','Release')][string]$Configuration = 'Release')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
Push-Location $projectRoot
try {
    & dotnet test tests\WorkspaceManager.Tests -c $Configuration -p:Platform=x64 --logger 'console;verbosity=normal' --logger 'trx;LogFileName=unit-tests.trx' --results-directory artifacts\validation
    if ($LASTEXITCODE -ne 0) { throw 'Unit tests failed.' }
    if ($Integration) {
        # Explicit opt-in: creates its own test window and a temporary desktop, restores the original desktop in finally.
        & dotnet run --project tools\WorkspaceManager.Diagnostics -c $Configuration -p:Platform=x64 -- --exercise
        if ($LASTEXITCODE -ne 0) { throw 'Interactive integration suite failed. See output.' }
    }
    if ($Preview) {
        & dotnet run --project tools\WorkspaceManager.Diagnostics -c $Configuration -p:Platform=x64 -- --preview-check
        if ($LASTEXITCODE -ne 0) { throw 'Preview regression suite failed.' }
    }
    if ($Close) {
        & dotnet run --project tools\WorkspaceManager.Diagnostics -c $Configuration -p:Platform=x64 -- --close-check
        if ($LASTEXITCODE -ne 0) { throw 'Close-window regression suite failed.' }
    }
    if ($UiSmoke -or $UiInteraction -or $UiClose) {
        & "$PSScriptRoot\Build.ps1" -Configuration $Configuration
        $exe = Join-Path $projectRoot "src\WorkspaceManager.App\bin\x64\$Configuration\net10.0-windows10.0.26100.0\win-x64\WorkspaceManager.App.exe"
        $modes = @()
        if ($UiSmoke) { $modes += 'smoke' }
        if ($UiInteraction) { $modes += 'interaction' }
        foreach ($mode in $modes) {
        $started = Get-Date
        $testProcess = Start-Process -FilePath $exe -ArgumentList "--$mode-test" -WindowStyle Hidden -PassThru
        if (!$testProcess.WaitForExit(30000)) { throw "UI initialization test timed out, PID $($testProcess.Id)." }
        $reportPath = Join-Path $env:LOCALAPPDATA "DesktopWorkspaceManager\ui-$mode-test.json"
        if ($testProcess.ExitCode -ne 0 -or !(Test-Path $reportPath) -or (Get-Item $reportPath).LastWriteTime -lt $started) { throw 'UI initialization test did not produce a successful fresh report.' }
        $report = Get-Content $reportPath -Raw | ConvertFrom-Json
        if (!$report.Success) { throw ($report | ConvertTo-Json -Depth 4) }
        Copy-Item -LiteralPath $reportPath -Destination (Join-Path $projectRoot "artifacts\validation\ui-$mode-test.json")
        $report | ConvertTo-Json -Depth 4
        }
        if ($UiClose) {
            $fixtureExe = Join-Path $projectRoot "tools\WorkspaceManager.Diagnostics\bin\x64\$Configuration\net10.0-windows10.0.26100.0\WorkspaceManager.Diagnostics.exe"
            $token = [Guid]::NewGuid().ToString('N')
            $fixtureProcess = Start-Process -FilePath $fixtureExe -ArgumentList "--close-fixture $token" -WindowStyle Hidden -PassThru
            $closeTestProcess = $null
            try {
                Start-Sleep -Milliseconds 1000
                $started = Get-Date
                $closeTestProcess = Start-Process -FilePath $exe -ArgumentList "--close-lifecycle-test $($fixtureProcess.Id) $token" -WindowStyle Hidden -PassThru
                if (!$closeTestProcess.WaitForExit(30000)) { throw 'Close lifecycle test timed out.' }
                $reportPath = Join-Path $env:LOCALAPPDATA 'DesktopWorkspaceManager\ui-close-lifecycle-test.json'
                if ($closeTestProcess.ExitCode -ne 0 -or !(Test-Path $reportPath) -or (Get-Item $reportPath).LastWriteTime -lt $started) { throw 'Close lifecycle test failed or did not produce a fresh report.' }
                $report = Get-Content $reportPath -Raw | ConvertFrom-Json
                if (!$report.Success) { throw ($report | ConvertTo-Json -Depth 4) }
                Copy-Item -LiteralPath $reportPath -Destination (Join-Path $projectRoot 'artifacts\validation\ui-close-lifecycle-test.json')
                $report | ConvertTo-Json -Depth 4
            } finally {
                # Only processes created by this test invocation may be cleaned up.
                foreach ($ownedProcess in @($closeTestProcess, $fixtureProcess)) {
                    if ($null -ne $ownedProcess -and !$ownedProcess.HasExited) { Stop-Process -Id $ownedProcess.Id }
                }
            }
        }
    }
} finally { Pop-Location }
