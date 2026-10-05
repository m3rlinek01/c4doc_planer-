<#
.SYNOPSIS
  Buduje paczkę C4 GuestPass dla serwera Linux (z Gamanet C4 SDK) – uruchamiać na Windows z zainstalowanym C4 SDK.

.DESCRIPTION
  Wynik: dist\c4guestpass-linux-x64.tar.gz z programem (samodzielny – na serwerze nie trzeba instalować .NET),
  instalatorem install.sh, usługą systemd i wzorem konfiguracji. Na serwerze:

    tar xzf c4guestpass-linux-x64.tar.gz && cd c4guestpass-linux-x64
    sudo bash install.sh --domain guestpass.firma.pl

  Pakiet SDK Gamanet jest licencjonowany – paczka zawiera jego biblioteki, więc nie publikuj jej poza firmą.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File deploy\build-linux.ps1
.EXAMPLE
  powershell -ExecutionPolicy Bypass -File deploy\build-linux.ps1 -Runtime linux-arm64 -C4SdkVersion None   # demo bez C4
#>
param(
  [ValidateSet('2024', '2026', 'None')] [string] $C4SdkVersion = '2024',
  [ValidateSet('linux-x64', 'linux-arm64')] [string] $Runtime = 'linux-x64',
  # Katalog z pakietami C4 SDK (układ id/wersja/*.nupkg) – domyślnie instalacja C4 SDK.
  [string] $C4SdkLocalFeed = 'C:\Program Files (x86)\Gamanet\C4 SDK'
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$name = "c4guestpass-$Runtime"
$out = Join-Path $root "dist\$name"

if ($C4SdkVersion -eq '2024' -and -not (Test-Path (Join-Path $C4SdkLocalFeed 'gamanet.c4.simpleclient'))) {
  throw "Nie znaleziono C4 SDK w '$C4SdkLocalFeed'. Zainstaluj Gamanet C4 SDK albo podaj -C4SdkLocalFeed."
}

Remove-Item -Recurse -Force $out -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $out | Out-Null

Write-Host "==> dotnet publish ($Runtime, C4 SDK $C4SdkVersion)" -ForegroundColor Green
dotnet publish (Join-Path $root 'src\C4GuestPass.Web\C4GuestPass.Web.csproj') -c Release -r $Runtime --self-contained `
  -p:C4SdkVersion=$C4SdkVersion "-p:C4SdkLocalFeed=$C4SdkLocalFeed" -p:DebugType=None -o (Join-Path $out 'app')
if ($LASTEXITCODE -ne 0) { throw "dotnet publish zakończony błędem $LASTEXITCODE" }

Remove-Item (Join-Path $out 'app\appsettings.Development.json') -ErrorAction SilentlyContinue
if ($Runtime -eq 'linux-arm64') { New-Item -ItemType File (Join-Path $out 'app\.arm64') | Out-Null }
Copy-Item (Join-Path $PSScriptRoot 'linux\*') $out
Set-Content -Encoding ascii (Join-Path $out 'app\BUILD.txt') @"
C4 GuestPass $Runtime
C4 SDK: $C4SdkVersion
Commit: $(git -C $root rev-parse --short HEAD 2>$null)
Zbudowano: $(Get-Date -Format 'yyyy-MM-dd HH:mm')
"@

# Skrypty z Windows mają CRLF – bash na Linuksie by się na nich wyłożył.
Get-ChildItem $out -File | Where-Object { $_.Extension -in '.sh', '.service', '.example' } | ForEach-Object {
  $text = [IO.File]::ReadAllText($_.FullName) -replace "`r`n", "`n"
  [IO.File]::WriteAllText($_.FullName, $text, (New-Object Text.UTF8Encoding $false))
}

$tgz = Join-Path $root "dist\$name.tar.gz"
Remove-Item $tgz -ErrorAction SilentlyContinue
Write-Host "==> $tgz" -ForegroundColor Green
tar -czf $tgz -C (Join-Path $root 'dist') $name
if ($LASTEXITCODE -ne 0) { throw "tar zakończony błędem $LASTEXITCODE" }
Write-Host ("Gotowe: {0} ({1:N1} MB)" -f $tgz, ((Get-Item $tgz).Length / 1MB)) -ForegroundColor Green
Write-Host "Na serwerze: tar xzf $name.tar.gz && cd $name && sudo bash install.sh --domain guestpass.firma.pl"
