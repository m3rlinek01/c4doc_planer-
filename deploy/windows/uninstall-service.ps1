#Requires -Version 5.1
#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Usuwa usługę Windows C4 GuestPass zainstalowaną przez install-service.ps1.

.DESCRIPTION
    Domyślnie usuwa tylko usługę i regułę zapory. Pliki programu (-RemoveFiles) i dane (-RemoveData:
    baza SQLite z historią wizyt, poczta Pickup) są kasowane wyłącznie na wyraźne żądanie.

    UWAGA: przed odinstalowaniem upewnij się, że w C4 nie zostały aktywne osoby-goście. Aplikacja usuwa je
    z C4 po zakończeniu wizyty – gdy usługa nie działa, nikt tego nie zrobi. Najprościej: przed odinstalowaniem
    unieważnij w aplikacji aktywne wizyty (unieważnienie od razu usuwa osobę i identyfikator z C4)
    albo usuń osoby ręcznie z folderów gości w C4.

.EXAMPLE
    .\uninstall-service.ps1
.EXAMPLE
    .\uninstall-service.ps1 -RemoveFiles -RemoveData
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [string] $ServiceName = 'C4GuestPass',
    [string] $DisplayName = 'C4 GuestPass',
    [string] $InstallDir = (Join-Path $env:ProgramFiles 'C4GuestPass'),
    [string] $DataDir = (Join-Path $env:ProgramData 'C4GuestPass'),
    [switch] $RemoveFiles,
    [switch] $RemoveData
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Write-Step([string] $msg) { Write-Host "==> $msg" -ForegroundColor Cyan }

$svc = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($svc) {
    if ($svc.Status -ne 'Stopped') {
        Write-Step "Zatrzymuję usługę $ServiceName"
        Stop-Service -Name $ServiceName -Force
        $svc.WaitForStatus('Stopped', [TimeSpan]::FromSeconds(60))
    }
    if ($PSCmdlet.ShouldProcess($ServiceName, 'Usunięcie usługi')) {
        Write-Step "Usuwam usługę $ServiceName"
        & sc.exe delete $ServiceName | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "sc.exe delete zakończył się kodem $LASTEXITCODE." }
    }
}
else {
    Write-Warning "Usługa $ServiceName nie istnieje."
}

$rules = Get-NetFirewallRule -Group $DisplayName -ErrorAction SilentlyContinue
if ($rules -and $PSCmdlet.ShouldProcess($DisplayName, 'Usunięcie reguł zapory')) {
    Write-Step 'Usuwam reguły zapory'
    $rules | Remove-NetFirewallRule
}

if ($RemoveFiles -and (Test-Path $InstallDir) -and $PSCmdlet.ShouldProcess($InstallDir, 'Usunięcie plików programu')) {
    Write-Step "Usuwam $InstallDir (łącznie z appsettings.Production.json)"
    Remove-Item $InstallDir -Recurse -Force
}

if ($RemoveData -and (Test-Path $DataDir) -and $PSCmdlet.ShouldProcess($DataDir, 'Usunięcie danych (baza SQLite)')) {
    Write-Step "Usuwam dane $DataDir"
    Remove-Item $DataDir -Recurse -Force
}

Write-Host 'Gotowe.' -ForegroundColor Green
if (-not $RemoveData) { Write-Host "Dane pozostawiono w $DataDir (usuń je parametrem -RemoveData)." }
if (-not $RemoveFiles) { Write-Host "Pliki programu pozostawiono w $InstallDir (usuń je parametrem -RemoveFiles)." }
