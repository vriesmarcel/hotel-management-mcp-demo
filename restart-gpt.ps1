# Run from an external PowerShell window, after active Codex tasks finish.
# Targets the installed Codex desktop app, including its child processes.
[CmdletBinding(SupportsShouldProcess = $true)]
param()

$ErrorActionPreference = 'Stop'
$codexPackage = Get-AppxPackage -Name OpenAI.Codex | Select-Object -First 1
if (-not $codexPackage) {
    throw 'The OpenAI.Codex desktop package was not found for this Windows user.'
}

$codexExe = Join-Path $codexPackage.InstallLocation 'app\ChatGPT.exe'
if (-not (Test-Path -LiteralPath $codexExe -PathType Leaf)) {
    throw 'The Codex desktop executable was not found in its installed package.'
}

$codexProcesses = @(Get-CimInstance Win32_Process |
    Where-Object { $_.ExecutablePath -eq $codexExe })
$codexRoots = @($codexProcesses |
    Where-Object { $_.ParentProcessId -notin $codexProcesses.ProcessId })

if ($PSCmdlet.ShouldProcess('Codex desktop', 'Close its process trees and reopen the app')) {
    foreach ($codexRoot in $codexRoots) {
        Write-Host "Closing Codex process tree $($codexRoot.ProcessId)..."
        & "$env:SystemRoot\System32\taskkill.exe" /PID $codexRoot.ProcessId /T /F
        if ($LASTEXITCODE -ne 0) {
            throw 'Could not close the Codex process tree. Reopening was skipped.'
        }
    }

    if ($codexRoots.Count -gt 0) {
        Start-Sleep -Seconds 2
    }

    # This is the visible interactive application the user wants to reopen.
    Start-Process -FilePath $codexExe
    Write-Host 'Codex launch requested. Keep your hotel host running and open a fresh chat.'
}
